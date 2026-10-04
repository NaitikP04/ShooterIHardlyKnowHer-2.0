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
    ///
    /// While held it also knows which inventory slot it occupies. Whether that slot is the
    /// one the holder has selected is the holder's business (PlayerWeapon); the item only
    /// asks, so it can hide itself when stowed.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class OneOffWeapon : NetworkBehaviour
    {
        public enum State : byte { OnGround, Held, InFlight }

        /// <summary>Ballistic = a hand-over toss. Boomerang = the attack: out, curve, and back.</summary>
        public enum FlightMode : byte { Ballistic, Boomerang }

        public struct ThrowData : INetworkSerializable
        {
            public Vector3 Origin;
            public Vector3 Velocity;   // Ballistic: launch velocity. Boomerang: direction * range.
            public double LaunchTime;
            public ulong Thrower;
            public FlightMode Mode;

            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref Origin);
                s.SerializeValue(ref Velocity);
                s.SerializeValue(ref LaunchTime);
                s.SerializeValue(ref Thrower);
                s.SerializeValue(ref Mode);
            }
        }

        /// <summary>Every spawned one-off on this peer.</summary>
        public static readonly List<OneOffWeapon> All = new();

        [SerializeField] private WeaponDefinition _definition;

        [Header("Throw")]
        // 15 m/s at ~24 degrees carries about 20 m on level aim in ~1.3 s: quick, but long
        // enough to see it coming and press the catch.
        [SerializeField, Min(1f)] private float _throwSpeed = 15f;
        [SerializeField, Range(0f, 1f), Tooltip("Added to the aim direction's Y so a flat throw still arcs")]
        private float _upwardBias = 0.45f;
        [SerializeField, Min(0f), Tooltip("Seconds after launch before the thrower can catch it back or get bonked")]
        private float _throwerGraceSeconds = 0.4f;

        [Header("Catch / bonk")]
        [SerializeField, Min(0.1f), Tooltip("Catch succeeds if the arc passes this close to the head...")]
        private float _catchRadius = 2.2f;
        [SerializeField, Min(0f), Tooltip("...at any point within this many seconds from now")]
        private float _catchLeadSeconds = 0.7f;
        [SerializeField, Range(-1f, 1f), Tooltip("Dot(player forward, toward weapon) needed to catch")]
        private float _catchFacingDot = 0.3f;
        [SerializeField, Min(0.1f), Tooltip("Reaching this close to the head or torso without being caught is a bonk")]
        private float _bonkRadius = 0.9f;
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
        private readonly NetworkVariable<int> _slot = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        // The slot this item was first put in. It goes back there whenever that's free, so
        // "3 is the boomerang" stays true across throws and catches.
        private readonly NetworkVariable<int> _preferredSlot = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<Vector3> _groundPosition = new(
            Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ThrowData> _throw = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Ammo belongs to the item: a half-empty gun thrown across arrives half-empty.
        private readonly NetworkVariable<int> _ammo = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<double> _reloadEndTime = new(
            0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Heat model: 0 cold .. 1 overheated. The lock covers both forced vents and manual ones.
        private readonly NetworkVariable<float> _heat = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<double> _heatLockEndTime = new(
            0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _overheated = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private double _lastShotTime;

        [Header("Boomerang attack")]
        [SerializeField, Min(0.05f)] private float _cutRadius = 0.6f;

        [Header("Thrown at an enemy")]
        [SerializeField, Min(0f), Tooltip("A hand-tossed weapon that hits an enemy does this much Blunt and drops there")]
        private float _throwBonkDamage = 6f;

        private Renderer[] _renderers;
        private float _groundedSince;
        private Vector3 _lastServerPosition;
        private bool _onReturnLeg;
        private readonly HashSet<NetworkObject> _cutThisLeg = new();

        public WeaponDefinition Definition => _definition;
        public State CurrentState => _state.Value;
        public ulong Holder => _holder.Value;
        public int Slot => _slot.Value;
        public bool IsHeld => _state.Value == State.Held;

        // ---- Ammo (same answer on every peer) ----------------------------------------
        public int Ammo => _ammo.Value;
        public int Magazine => _definition.MagazineSize;
        public bool IsReloading => _reloadEndTime.Value > 0d && NetworkManager.ServerTime.Time < _reloadEndTime.Value;
        public float ReloadProgress => IsReloading
            ? 1f - Mathf.Clamp01((float)(_reloadEndTime.Value - NetworkManager.ServerTime.Time) / _definition.ReloadSeconds)
            : 0f;
        // ---- Heat (same answer on every peer) ----------------------------------------
        public float Heat => _heat.Value;
        public bool IsHeatLocked => _heatLockEndTime.Value > 0d && NetworkManager.ServerTime.Time < _heatLockEndTime.Value;
        /// <summary>True during a forced vent after redlining; false during a manual vent.</summary>
        public bool IsOverheated => _overheated.Value && IsHeatLocked;
        /// <summary>x1 cold .. MaxHeatDamageMultiplier at the red line. Hotter is deadlier.</summary>
        public float HeatDamageMultiplier => _definition.UsesHeat
            ? Mathf.Lerp(1f, _definition.MaxHeatDamageMultiplier, _heat.Value)
            : 1f;

        public bool CanFire => _definition.Ammo switch
        {
            WeaponDefinition.AmmoKind.Infinite => true,
            WeaponDefinition.AmmoKind.Magazine => !IsReloading && _ammo.Value > 0,
            WeaponDefinition.AmmoKind.Heat => !IsHeatLocked,
            _ => true,
        };

        /// <summary>Server only. Spends a shot (or adds heat); false if the weapon can't fire right now.</summary>
        public bool TryConsumeAmmo()
        {
            switch (_definition.Ammo)
            {
                case WeaponDefinition.AmmoKind.Magazine:
                    if (IsReloading || _ammo.Value <= 0) return false;
                    _ammo.Value--;
                    return true;

                case WeaponDefinition.AmmoKind.Heat:
                    if (IsHeatLocked) return false;
                    _lastShotTime = NetworkManager.ServerTime.Time;
                    float heat = _heat.Value + _definition.HeatPerSecond * _definition.SecondsBetweenShots;
                    if (heat >= 1f)
                    {
                        // Redlined: the shot still fires (at full multiplier), then forced vent.
                        _heat.Value = 1f;
                        _overheated.Value = true;
                        _heatLockEndTime.Value = NetworkManager.ServerTime.Time + _definition.OverheatLockSeconds;
                    }
                    else
                    {
                        _heat.Value = heat;
                    }
                    return true;

                default:
                    return true;
            }
        }

        /// <summary>Server only. R: reload a magazine, or vent a heat weapon early.</summary>
        public void BeginReload()
        {
            switch (_definition.Ammo)
            {
                case WeaponDefinition.AmmoKind.Magazine:
                    if (IsReloading || _ammo.Value >= Magazine) return;
                    _reloadEndTime.Value = NetworkManager.ServerTime.Time + _definition.ReloadSeconds;
                    break;

                case WeaponDefinition.AmmoKind.Heat:
                    if (IsHeatLocked || _heat.Value <= 0.01f) return;
                    _overheated.Value = false;
                    _heatLockEndTime.Value = NetworkManager.ServerTime.Time + _definition.VentSeconds;
                    break;
            }
        }

        private void ServerTickReload()
        {
            double now = NetworkManager.ServerTime.Time;

            if (_reloadEndTime.Value > 0d && now >= _reloadEndTime.Value)
            {
                _ammo.Value = Magazine;
                _reloadEndTime.Value = 0d;
            }

            if (!_definition.UsesHeat) return;

            if (_heatLockEndTime.Value > 0d)
            {
                // Venting: heat drains to zero over the lock, then the lock lifts.
                float lockLength = _overheated.Value ? _definition.OverheatLockSeconds : _definition.VentSeconds;
                float remaining = (float)(_heatLockEndTime.Value - now);
                _heat.Value = Mathf.Clamp01(remaining / lockLength) * (_overheated.Value ? 1f : _heat.Value);
                if (now >= _heatLockEndTime.Value)
                {
                    _heat.Value = 0f;
                    _heatLockEndTime.Value = 0d;
                    _overheated.Value = false;
                }
            }
            else if (_heat.Value > 0f && now - _lastShotTime > 0.15d)
            {
                // Finger off the trigger: passive cooling.
                _heat.Value = Mathf.Max(0f, _heat.Value - _definition.CoolPerSecond * Time.deltaTime);
            }
        }

        // ---- Queries (same answer on every peer) ---------------------------------------

        public static OneOffWeapon HeldIn(ulong clientId, int slot)
        {
            foreach (var w in All)
            {
                if (w.IsHeld && w.Holder == clientId && w.Slot == slot) return w;
            }
            return null;
        }

        /// <summary>Fills <paramref name="results"/> with the client's held items, ordered by slot.</summary>
        public static void HeldBy(ulong clientId, List<OneOffWeapon> results)
        {
            results.Clear();
            foreach (var w in All)
            {
                if (w.IsHeld && w.Holder == clientId) results.Add(w);
            }
            results.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        }

        /// <summary>Lowest free one-off slot in 1..maxSlots, or -1 when the hands are full.</summary>
        public static int FirstFreeSlot(ulong clientId, int maxSlots)
        {
            for (int slot = 1; slot <= maxSlots; slot++)
            {
                if (HeldIn(clientId, slot) == null) return slot;
            }
            return -1;
        }

        /// <summary>
        /// Any peer. The slot this item would land in for <paramref name="clientId"/>: its
        /// remembered slot if free, else the first free one, else -1. Same answer everywhere,
        /// so the owner can predict what the server will do.
        /// </summary>
        public int SlotFor(ulong clientId, int maxSlots)
        {
            int preferred = _preferredSlot.Value;
            if (preferred >= 1 && preferred <= maxSlots && HeldIn(clientId, preferred) == null) return preferred;
            return FirstFreeSlot(clientId, maxSlots);
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
            if (IsServer && _definition.Ammo == WeaponDefinition.AmmoKind.Magazine) _ammo.Value = _definition.MagazineSize;
            _renderers = GetComponentsInChildren<Renderer>();
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, _definition.TracerColor);
            foreach (var r in _renderers) r.SetPropertyBlock(block);
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        // ---- Server API ---------------------------------------------------------------

        /// <summary>Server only. Place it on the ground here.</summary>
        public void Drop(Vector3 position)
        {
            position.y = _restingHeight;
            _groundPosition.Value = position;
            _holder.Value = ulong.MaxValue;
            _slot.Value = 0;
            _state.Value = State.OnGround;
            _groundedSince = Time.time;
        }

        /// <summary>Server only. Succeeds if the item is on the ground within reach and a slot is free.</summary>
        public bool TryPickUp(PlayerRig by, int maxSlots)
        {
            if (_state.Value != State.OnGround) return false;
            if ((by.transform.position - _groundPosition.Value).sqrMagnitude > _pickupRadius * _pickupRadius) return false;

            int slot = SlotFor(by.OwnerClientId, maxSlots);
            if (slot < 1) return false;
            Hold(by.OwnerClientId, slot);
            return true;
        }

        /// <summary>Server only. Only the holder can throw. A hand-over toss.</summary>
        public bool TryThrow(PlayerRig by, Vector3 direction)
        {
            if (_state.Value != State.Held || _holder.Value != by.OwnerClientId) return false;

            direction.Normalize();
            Vector3 velocity = (direction + Vector3.up * _upwardBias).normalized * _throwSpeed;
            Launch(by, by.Head.position + direction * 0.6f, velocity, FlightMode.Ballistic);
            return true;
        }

        /// <summary>Server only. Only the holder can throw. The boomerang attack: out and back.</summary>
        public bool TryThrowAttack(PlayerRig by, Vector3 direction)
        {
            if (_state.Value != State.Held || _holder.Value != by.OwnerClientId) return false;
            if (!_definition.ThrowToAttack) return false;

            // Follows the aim, pitch included: aim down to cut ground-level enemies. Thrown
            // from chest height so the near part of the arc still reaches short enemies; the
            // path itself refuses to go underground (see BoomerangPoint).
            direction.Normalize();
            Vector3 chest = by.Head.position + Vector3.down * 0.8f;
            Launch(by, chest + direction * 0.6f, direction * _definition.Range, FlightMode.Boomerang);
            return true;
        }

        private void Launch(PlayerRig by, Vector3 origin, Vector3 velocity, FlightMode mode)
        {
            _throw.Value = new ThrowData
            {
                Origin = origin,
                Velocity = velocity,
                LaunchTime = NetworkManager.ServerTime.Time,
                Thrower = by.OwnerClientId,
                Mode = mode,
            };
            _lastServerPosition = origin;
            _onReturnLeg = false;
            _cutThisLeg.Clear();
            _reloadEndTime.Value = 0d; // you can't reload a gun that's in the air (heat keeps cooling on its own)
            _holder.Value = ulong.MaxValue;
            _slot.Value = 0;
            _state.Value = State.InFlight;
        }

        /// <summary>
        /// Any peer. True if this flying item will pass within the catch radius of
        /// <paramref name="head"/> during the next catch window, and the player is facing it.
        /// Drives both the on-screen prompt and the server's verdict, from the same arc.
        /// </summary>
        public bool IsCatchableBy(PlayerRig rig)
        {
            if (_state.Value != State.InFlight) return false;

            double now = NetworkManager.ServerTime.Time;
            ThrowData t = _throw.Value;
            if (rig.OwnerClientId == t.Thrower && now - t.LaunchTime < _throwerGraceSeconds) return false;

            Vector3 head = rig.Head.position;
            float radiusSqr = _catchRadius * _catchRadius;
            const float step = 0.05f;
            for (float lead = 0f; lead <= _catchLeadSeconds; lead += step)
            {
                Vector3 p = FlightPosition(now + lead, out _);
                if ((p - head).sqrMagnitude > radiusSqr) continue;

                Vector3 flat = p - head;
                flat.y = 0f;
                return Vector3.Dot(rig.transform.forward, flat.normalized) >= _catchFacingDot;
            }
            return false;
        }

        /// <summary>Server only. The player pressed catch; succeed if the arc really is within reach and a slot is free.</summary>
        public bool TryCatch(PlayerRig by, int maxSlots)
        {
            if (!IsCatchableBy(by)) return false;
            int slot = SlotFor(by.OwnerClientId, maxSlots);
            if (slot < 1) return false;
            Hold(by.OwnerClientId, slot);
            return true;
        }

        private void Hold(ulong clientId, int slot)
        {
            _holder.Value = clientId;
            _slot.Value = slot;
            if (_preferredSlot.Value == 0) _preferredSlot.Value = slot;
            _state.Value = State.Held;
        }

        // ---- Simulation / presentation --------------------------------------------------

        private Vector3 FlightPosition(double now, out Vector3 velocity)
        {
            ThrowData t = _throw.Value;
            float dt = (float)(now - t.LaunchTime);

            if (t.Mode == FlightMode.Boomerang)
            {
                Vector3 p = BoomerangPoint(t, dt);
                velocity = (BoomerangPoint(t, dt + 0.02f) - p) / 0.02f;
                return p;
            }

            velocity = t.Velocity + Physics.gravity * dt;
            return t.Origin + t.Velocity * dt + 0.5f * Physics.gravity * (dt * dt);
        }

        /// <summary>
        /// Out along the throw direction and back along the same line, bulging sideways only
        /// near the turn. Both legs pass through what you aimed at, so a target straight ahead
        /// is cut on the way out and again on the way back. Clamped to the trip time so it
        /// rests at the origin once home.
        /// </summary>
        private Vector3 BoomerangPoint(ThrowData t, float elapsed)
        {
            float period = _definition.BoomerangSeconds;
            float u = Mathf.Clamp01(elapsed / period);           // 0 start, 0.5 furthest, 1 home
            Vector3 dir = t.Velocity.normalized;
            float range = t.Velocity.magnitude;
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            float along = range * Mathf.Sin(u * Mathf.PI);
            float bulge = Mathf.Sin(u * Mathf.PI);
            float side = range * _definition.BoomerangCurve * bulge * bulge * bulge; // stays near the line until the far end
            Vector3 p = t.Origin + dir * along + right * side;
            p.y = Mathf.Max(p.y, _restingHeight + 0.3f); // skim the ground, never tunnel
            return p;
        }

        private bool BoomerangHome(ThrowData t, double now) =>
            t.Mode == FlightMode.Boomerang && now - t.LaunchTime >= _definition.BoomerangSeconds;

        private void SetVisible(bool visible)
        {
            foreach (var r in _renderers) r.enabled = visible;
        }

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTickReload();

            switch (_state.Value)
            {
                case State.OnGround:
                    SetVisible(true);
                    transform.SetPositionAndRotation(_groundPosition.Value, Quaternion.Euler(0f, 0f, 90f));
                    if (IsServer) ServerCheckUnreachable();
                    break;

                case State.Held:
                    PlayerRig holder = FindRig(_holder.Value);
                    if (holder != null)
                    {
                        // Only the selected slot is in the hands; the rest are stowed out of sight.
                        bool selected = holder.TryGetComponent(out PlayerWeapon weapon) && weapon.SelectedSlot == _slot.Value;
                        SetVisible(selected);
                        transform.SetPositionAndRotation(holder.Head.TransformPoint(_heldOffset), holder.Head.rotation);
                    }
                    break;

                case State.InFlight:
                    SetVisible(true);
                    Vector3 position = FlightPosition(NetworkManager.ServerTime.Time, out Vector3 velocity);
                    Quaternion facing = velocity.sqrMagnitude > 0.01f ? Quaternion.LookRotation(velocity) : transform.rotation;
                    float spin = _throw.Value.Mode == FlightMode.Boomerang ? Time.time * 1440f : Time.time * 720f;
                    Vector3 spinAxis = _throw.Value.Mode == FlightMode.Boomerang ? Vector3.up : Vector3.forward;
                    transform.SetPositionAndRotation(position, facing * Quaternion.AngleAxis(spin, spinAxis));
                    if (IsServer) ServerResolveFlight(position, velocity);
                    break;
            }
        }

        private void ServerResolveFlight(Vector3 position, Vector3 velocity)
        {
            ThrowData t = _throw.Value;
            double now = NetworkManager.ServerTime.Time;
            float age = (float)(now - t.LaunchTime);

            if (t.Mode == FlightMode.Boomerang)
            {
                CutEnemiesAlongPath(position, velocity, t, age);
            }
            else if (BonkEnemyAlongPath(position, velocity, t))
            {
                return; // it hit something and dropped there
            }

            // Catching is a button press (TryCatch). Here we only resolve what happens when
            // nobody pressed it in time: the item reaches a head and bonks it.
            foreach (PlayerRig rig in PlayerRig.Active)
            {
                if (rig.OwnerClientId == t.Thrower && age < _throwerGraceSeconds) continue;

                // Head or torso: a weapon in the chest hurts just as much.
                Vector3 toWeapon = position - rig.Head.position;
                Vector3 toTorso = position - (rig.transform.position + Vector3.up * 1.1f);
                float bonkSqr = _bonkRadius * _bonkRadius;
                if (toWeapon.sqrMagnitude > bonkSqr && toTorso.sqrMagnitude > bonkSqr) continue;

                if (rig.TryGetComponent(out Health health))
                {
                    health.TakeDamage(new DamageInfo(_bonkDamage, DamageType.Blunt, position, velocity.normalized, t.Thrower));
                }
                Vector3 flat = new(toWeapon.x, 0f, toWeapon.z);
                Drop(rig.transform.position + flat.normalized * 0.8f);
                return;
            }

            if (position.y <= _restingHeight || BoomerangHome(t, now))
            {
                // A boomerang nobody caught comes to rest where it was thrown from.
                Drop(position);
            }
        }

        /// <summary>
        /// Hand-toss only. If the arc passes through an enemy, clonk it and drop right there.
        /// A thrown gun is a blunt instrument: same damage whatever the weapon.
        /// </summary>
        private bool BonkEnemyAlongPath(Vector3 position, Vector3 velocity, ThrowData t)
        {
            Vector3 travel = position - _lastServerPosition;
            float distance = travel.magnitude;
            _lastServerPosition = position;
            if (distance <= 0.0001f) return false;

            if (!Physics.SphereCast(position - travel, _cutRadius, travel / distance, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (hit.collider.GetComponentInParent<PlayerRig>() != null) return false;
            if (hit.collider.GetComponentInParent<IDamageable>() is not { } target) return false;

            target.TakeDamage(new DamageInfo(_throwBonkDamage, DamageType.Blunt, hit.point, velocity.normalized, t.Thrower));
            Drop(hit.point - velocity.normalized * 0.5f);
            return true;
        }

        /// <summary>
        /// Boomerang only. Sweep the path since last frame and hurt every enemy it passes,
        /// once per leg. Players are deliberately excluded: for them the weapon is a catch
        /// or a bonk, never a cut.
        /// </summary>
        private void CutEnemiesAlongPath(Vector3 position, Vector3 velocity, ThrowData t, float age)
        {
            bool returnLeg = age >= 0.5f * _definition.BoomerangSeconds;
            if (returnLeg && !_onReturnLeg)
            {
                _onReturnLeg = true;
                _cutThisLeg.Clear(); // second chance at everything on the way back
            }

            Vector3 travel = position - _lastServerPosition;
            float distance = travel.magnitude;
            if (distance > 0.0001f)
            {
                var hits = Physics.SphereCastAll(_lastServerPosition, _cutRadius, travel / distance, distance, ~0, QueryTriggerInteraction.Ignore);
                foreach (var hit in hits)
                {
                    if (hit.collider.GetComponentInParent<PlayerRig>() != null) continue;
                    var obj = hit.collider.GetComponentInParent<NetworkObject>();
                    if (obj == null || _cutThisLeg.Contains(obj)) continue;
                    if (hit.collider.GetComponentInParent<IDamageable>() is not { } target) continue;

                    _cutThisLeg.Add(obj);
                    target.TakeDamage(new DamageInfo(_definition.Damage, _definition.DamageType, hit.point, velocity.normalized, t.Thrower));
                }
            }
            _lastServerPosition = position;
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
