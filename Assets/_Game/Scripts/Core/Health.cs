using System;
using Unity.Netcode;
using UnityEngine;

namespace SIHKH.Core
{
    /// <summary>
    /// Hit points for anything: players, enemies, later destructibles. The server is the
    /// only writer; everyone can read the current value for bars and tints. Death is an
    /// event so the owner (Enemy, PlayerRig...) decides what dying means.
    /// </summary>
    public class Health : NetworkBehaviour, IDamageable
    {
        [SerializeField, Min(1f)] private float _maxHealth = 10f;

        private readonly NetworkVariable<float> _current = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Server only. Fired once, with the killing blow.</summary>
        public event Action<DamageInfo> Died;

        /// <summary>Everyone. (previous, current)</summary>
        public event Action<float, float> Changed;

        public float Max => _maxHealth;
        public float Current => _current.Value;
        public float Normalized => _current.Value / _maxHealth;
        public bool IsDead => IsSpawned && _current.Value <= 0f;

        /// <summary>Set before spawn (e.g. from an EnemyDefinition) so the initial value is right.</summary>
        public void Configure(float maxHealth) => _maxHealth = Mathf.Max(1f, maxHealth);

        public override void OnNetworkSpawn()
        {
            if (IsServer) _current.Value = _maxHealth;
            _current.OnValueChanged += OnCurrentChanged;
        }

        public override void OnNetworkDespawn() => _current.OnValueChanged -= OnCurrentChanged;

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsServer || IsDead) return;

            _current.Value = Mathf.Max(0f, _current.Value - info.Amount);
            if (_current.Value <= 0f) Died?.Invoke(info);
        }

        /// <summary>Server only.</summary>
        public void ResetToFull()
        {
            if (IsServer) _current.Value = _maxHealth;
        }

        private void OnCurrentChanged(float previous, float current) => Changed?.Invoke(previous, current);
    }
}
