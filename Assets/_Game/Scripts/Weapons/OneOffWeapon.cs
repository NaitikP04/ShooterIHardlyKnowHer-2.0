using System.Collections.Generic;
using SIHKH.Core;
using SIHKH.Player;
using Unity.Netcode;
using UnityEngine;

namespace SIHKH.Weapons
{
    /// <summary>
    /// A single shared weapon that lives in the world: on the ground, in someone's hands,
    /// or flying across the gap. The server owns the state. In flight nothing is moved over
    /// the network: the server publishes the launch (origin, velocity, time) once and every
    /// peer computes the same arc from the shared clock.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class OneOffWeapon : NetworkBehaviour
    {
        public enum State : byte { OnGround, Held, InFlight }

        public struct ThrowData : INetworkSerializable
        {
            public Vector3 Origin;
            public Vector3 Velocity;
            public double LaunchTime;
            public ulong Thrower;

            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref Origin);
                s.SerializeValue(ref Velocity);
                s.SerializeValue(ref LaunchTime);
                s.SerializeValue(ref Thrower);
            }
        }

        /// <summary>Every spawned one-off on this peer.</summary>
        public static readonly List<OneOffWeapon> All = new();

        [SerializeField] private WeaponDefinition _definition;

        [Header("Throw")]
        // 18 m/s at ~17 degrees carries about 23 m on level aim: a straight throw at your
        // partner across the 20 m gap arrives; aim high or low to over/undershoot.
        [SerializeField, Min(1f)] private float _throwSpeed = 18f;
        [SerializeField, Range(0f, 1f), Tooltip("Added to the aim direction's Y so a flat throw still arcs")]
        private float _upwardBias = 0.3f;
        [SerializeField, Min(0f), Tooltip("Seconds after launch before the thrower can catch it back")]
        private float _throwerGraceSeconds = 0.4f;

        [Header("Catch / bonk")]
        [SerializeField, Min(0.1f)] private float _catchRadius = 1.3f;
        [SerializeField, Range(-1f, 1f), Tooltip("Dot(player forward, toward weapon) needed to catch instead of getting bonked")]
        private float _catchFacingDot = 0.3f;
        [SerializeField, Min(0f)] private float _bonkDamage = 2f;

        [Header("Ground")]
        [SerializeField, Min(0.1f)] private float _pickupRadius = 2.5f;
        [SerializeField] private float _restingHeight = 0.3f;
        [SerializeField, Min(1f), Tooltip("Lying unreachable this long sends it back to whoever threw it")]
        private float _unreachableResetSeconds = 8f;

        [Header("Held pose (local to holder's head)")]
        [SerializeField] private Vector3 _heldOffset = new(0.22f, -0.2f, 0.42f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly NetworkVariable<State> _state = new(
            State.OnGround, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ulong> _holder = new(
            ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<Vector3> _groundPosition = new(
            Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ThrowData> _throw = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private float _groundedSince;

        public WeaponDefinition Definition => _definition;
        public State CurrentState => _state.Value;
        public ulong Holder => _holder.Value;
        public bool IsHeld => _state.Value == State.Held;

        public static OneOffWeapon HeldBy(ulong clientId)
        {
            foreach (var w in All)
            {
                if (w.IsHeld && w.Holder == clientId) return w;
            }
            return null;
        }

        public static OneOffWeapon NearestOnGround(Vector3 position, float maxDistance)
        {
            OneOffWeapon best = null;
            float bestSqr = maxDistance * maxDistance;
            foreach (var w in All)
            {
                if (w.CurrentState != State.OnGround) continue;
                float sqr = (w._groundPosition.Value - position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = w;
                }
            }
            return best;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, _definition.TracerColor);
            foreach (var r in GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(block);
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        // ---- Server API ---------------------------------------------------------------

        /// <summary>Server only. Place it on the ground here.</summary>
        public void Drop(Vector3 position)
        {
            position.y = _restingHeight;
            _groundPosition.Value = position;
            _holder.Value = ulong.MaxValue;
            _state.Value = State.OnGround;
            _groundedSince = Time.time;
        }

        /// <summary>Server only. Succeeds if the item is on the ground within reach of the player.</summary>
        public bool TryPickUp(PlayerRig by)
        {
            if (_state.Value != State.OnGround) return false;
            if ((by.transform.position - _groundPosition.Value).sqrMagnitude > _pickupRadius * _pickupRadius) return false;

            _holder.Value = by.OwnerClientId;
            _state.Value = State.Held;
            return true;
        }

        /// <summary>Server only. Only the holder can throw.</summary>
        public bool TryThrow(PlayerRig by, Vector3 direction)
        {
            if (_state.Value != State.Held || _holder.Value != by.OwnerClientId) return false;

            direction.Normalize();
            Vector3 velocity = (direction + Vector3.up * _upwardBias).normalized * _throwSpeed;
            _throw.Value = new ThrowData
            {
                Origin = by.Head.position + direction * 0.6f,
                Velocity = velocity,
                LaunchTime = NetworkManager.ServerTime.Time,
                Thrower = by.OwnerClientId,
            };
            _holder.Value = ulong.MaxValue;
            _state.Value = State.InFlight;
            return true;
        }

        // ---- Simulation / presentation --------------------------------------------------

        private Vector3 FlightPosition(double now, out Vector3 velocity)
        {
            ThrowData t = _throw.Value;
            float dt = (float)(now - t.LaunchTime);
            velocity = t.Velocity + Physics.gravity * dt;
            return t.Origin + t.Velocity * dt + 0.5f * Physics.gravity * (dt * dt);
        }

        private void Update()
        {
            if (!IsSpawned) return;

            switch (_state.Value)
            {
                case State.OnGround:
                    transform.SetPositionAndRotation(_groundPosition.Value, Quaternion.Euler(0f, 0f, 90f));
                    if (IsServer) ServerCheckUnreachable();
                    break;

                case State.Held:
                    PlayerRig holder = FindRig(_holder.Value);
                    if (holder != null)
                    {
                        transform.SetPositionAndRotation(holder.Head.TransformPoint(_heldOffset), holder.Head.rotation);
                    }
                    break;

                case State.InFlight:
                    Vector3 position = FlightPosition(NetworkManager.ServerTime.Time, out Vector3 velocity);
                    transform.SetPositionAndRotation(position, Quaternion.LookRotation(velocity) * Quaternion.Euler(0f, 0f, Time.time * 720f));
                    if (IsServer) ServerResolveFlight(position, velocity);
                    break;
            }
        }

        private void ServerResolveFlight(Vector3 position, Vector3 velocity)
        {
            ThrowData t = _throw.Value;
            float age = (float)(NetworkManager.ServerTime.Time - t.LaunchTime);

            foreach (PlayerRig rig in PlayerRig.Active)
            {
                if (rig.OwnerClientId == t.Thrower && age < _throwerGraceSeconds) continue;

                Vector3 toWeapon = position - rig.Head.position;
                if (toWeapon.sqrMagnitude > _catchRadius * _catchRadius) continue;

                Vector3 flat = new(toWeapon.x, 0f, toWeapon.z);
                bool facing = Vector3.Dot(rig.transform.forward, flat.normalized) >= _catchFacingDot;

                if (facing)
                {
                    _holder.Value = rig.OwnerClientId;
                    _state.Value = State.Held;
                }
                else
                {
                    // Bonk: hit them, drop at their feet on the side it came from.
                    if (rig.TryGetComponent(out Health health))
                    {
                        health.TakeDamage(new DamageInfo(_bonkDamage, DamageType.Blunt, position, velocity.normalized, t.Thrower));
                    }
                    Drop(rig.transform.position + flat.normalized * 0.8f);
                }
                return;
            }

            if (position.y <= _restingHeight)
            {
                Drop(position);
            }
        }

        private void ServerCheckUnreachable()
        {
            if (Time.time - _groundedSince < _unreachableResetSeconds) return;

            foreach (PlayerRig rig in PlayerRig.Active)
            {
                if ((rig.transform.position - _groundPosition.Value).sqrMagnitude <= 4f * _pickupRadius * _pickupRadius)
                {
                    _groundedSince = Time.time; // someone's close enough; give them time
                    return;
                }
            }

            // Nobody can get it: send it back to the last thrower, or the first player.
            PlayerRig target = FindRig(_throw.Value.Thrower) ?? (PlayerRig.Active.Count > 0 ? PlayerRig.Active[0] : null);
            if (target != null) Drop(target.transform.position + target.transform.forward * 0.8f);
        }

        private static PlayerRig FindRig(ulong clientId)
        {
            foreach (var rig in PlayerRig.Active)
            {
                if (rig.OwnerClientId == clientId) return rig;
            }
            return null;
        }
    }
}
