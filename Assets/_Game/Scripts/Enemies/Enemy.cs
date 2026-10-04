using SIHKH.Core;
using SIHKH.Player;
using SIHKH.UI;
using Unity.Netcode;
using UnityEngine;

namespace SIHKH.Enemies
{
    /// <summary>
    /// One enemy. The server picks the nearest player, moves at them (walking, flying,
    /// weaving, per its definition), and hits them on arrival. Position reaches clients
    /// through NetworkTransform; health through the Health component, which this enemy
    /// gives a resistance modifier. Dying means despawning.
    /// </summary>
    [RequireComponent(typeof(NetworkObject), typeof(Health))]
    public class Enemy : NetworkBehaviour
    {
        [SerializeField] private EnemyDefinition _definition;
        [SerializeField, Min(0.01f)] private float _hitFlashSeconds = 0.12f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Health _health;
        private PlayerRig _target;
        private float _nextAttackTime;
        private float _flashUntil;
        private float _weavePhase;
        private Renderer[] _renderers;

        public EnemyDefinition Definition => _definition;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _health.Configure(_definition.MaxHealth);
            _weavePhase = Random.Range(0f, Mathf.PI * 2f); // so a pack doesn't weave in lockstep
        }

        public override void OnNetworkSpawn()
        {
            _renderers = GetComponentsInChildren<Renderer>();
            Tint(_definition.Tint);

            _health.Damaged += OnDamaged;
            if (IsServer)
            {
                _health.Modifier = info => _definition.MultiplierFor(info.Type);
                _health.Died += OnDied;
            }
        }

        public override void OnNetworkDespawn()
        {
            _health.Damaged -= OnDamaged;
            if (IsServer)
            {
                _health.Modifier = null;
                _health.Died -= OnDied;
            }
        }

        private void OnDamaged(Vector3 point, float applied, float multiplier)
        {
            DamageNumber.Spawn(point, applied, multiplier);
            _flashUntil = Time.time + _hitFlashSeconds;
            // The flash echoes the number: dim for resisted, white for normal, gold for a weak spot.
            Tint(multiplier < 0.95f ? new Color(0.45f, 0.45f, 0.45f)
               : multiplier > 1.05f ? new Color(1f, 0.85f, 0.2f)
               : Color.white);
        }

        private void Tint(Color color)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, color);
            foreach (var r in _renderers) r.SetPropertyBlock(block);
        }

        private void Update()
        {
            if (_flashUntil > 0f && Time.time >= _flashUntil)
            {
                _flashUntil = 0f;
                Tint(_definition.Tint);
            }

            if (!IsSpawned || !IsServer || _health.IsDead) return;

            if (_target == null || !_target.IsSpawned) _target = FindNearestPlayer();
            if (_target == null) return;

            Vector3 toTarget = _target.transform.position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            if (distance > _definition.ReachDistance)
            {
                Move(toTarget / distance);
                return;
            }

            if (Time.time >= _nextAttackTime)
            {
                _nextAttackTime = Time.time + _definition.AttackInterval;
                if (_target.TryGetComponent(out Health targetHealth))
                {
                    targetHealth.TakeDamage(new DamageInfo(
                        _definition.AttackDamage, DamageType.Blunt, _target.transform.position, toTarget.normalized,
                        NetworkManager.ServerClientId));
                }
            }
        }

        private void Move(Vector3 forward)
        {
            Vector3 step = forward * (_definition.MoveSpeed * Time.deltaTime);

            if (_definition.WeaveAmplitude > 0f)
            {
                // Sideways sine on top of the approach: erratic without being random, so the
                // server stays the only simulation and clients just see the result.
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                float phase = (Time.time * _definition.WeaveFrequency + _weavePhase) * Mathf.PI * 2f;
                step += right * (Mathf.Cos(phase) * _definition.WeaveAmplitude * _definition.WeaveFrequency * Mathf.PI * 2f * Time.deltaTime);
            }

            Vector3 next = transform.position + step;
            float groundY = GetComponent<Collider>() is { } c ? c.bounds.extents.y : 0.5f;
            next.y = _definition.FlyHeight > 0f ? _definition.FlyHeight : groundY;

            transform.position = next;
            transform.rotation = Quaternion.LookRotation(forward);
        }

        private PlayerRig FindNearestPlayer()
        {
            PlayerRig best = null;
            float bestSqr = float.MaxValue;
            foreach (var rig in PlayerRig.Active)
            {
                float sqr = (rig.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = rig;
                }
            }
            return best;
        }

        private void OnDied(DamageInfo killingBlow)
        {
            // Later: ragdoll + drop roll here. For now it just stops existing.
            NetworkObject.Despawn(destroy: true);
        }
    }
}
