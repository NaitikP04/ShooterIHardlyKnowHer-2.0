using SIHKH.Core;
using SIHKH.Player;
using Unity.Netcode;
using UnityEngine;

namespace SIHKH.Enemies
{
    /// <summary>
    /// A walking cube, for now. The server picks the nearest player, walks straight at
    /// them, and hits them on arrival. Position reaches clients through NetworkTransform;
    /// health through the Health component. Dying means despawning.
    /// </summary>
    [RequireComponent(typeof(NetworkObject), typeof(Health))]
    public class Enemy : NetworkBehaviour
    {
        [SerializeField] private EnemyDefinition _definition;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField, Min(0.01f)] private float _hitFlashSeconds = 0.12f;

        private Health _health;
        private PlayerRig _target;
        private float _nextAttackTime;
        private float _flashUntil;
        private Renderer[] _renderers;

        public EnemyDefinition Definition => _definition;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _health.Configure(_definition.MaxHealth);
        }

        public override void OnNetworkSpawn()
        {
            _renderers = GetComponentsInChildren<Renderer>();
            Tint(_definition.Tint);

            // Everyone sees the health drop, so everyone can flash. No extra RPC needed.
            _health.Changed += OnHealthChanged;
            if (IsServer) _health.Died += OnDied;
        }

        public override void OnNetworkDespawn()
        {
            _health.Changed -= OnHealthChanged;
            if (IsServer) _health.Died -= OnDied;
        }

        private void OnHealthChanged(float previous, float current)
        {
            if (current >= previous) return;
            _flashUntil = Time.time + _hitFlashSeconds;
            Tint(Color.white);
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
                Vector3 step = toTarget / distance * (_definition.MoveSpeed * Time.deltaTime);
                transform.position += step;
                transform.rotation = Quaternion.LookRotation(toTarget);
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
            // Later: ragdoll + drop roll here. For now the cube just stops existing.
            NetworkObject.Despawn(destroy: true);
        }
    }
}
