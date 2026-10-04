using System.Collections.Generic;
using SIHKH.Core;
using SIHKH.Enemies;
using SIHKH.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SIHKH.Waves
{
    /// <summary>
    /// The run: waves in order, then escalation; a shared team health pool that any hit on
    /// either player drains; run over at zero; restart. Server-simulated, state replicated
    /// as a handful of NetworkVariables that the HUD reads.
    /// </summary>
    [RequireComponent(typeof(EnemySpawner))]
    public class WaveDirector : NetworkBehaviour
    {
        public enum Phase : byte { Idle, Spawning, Fighting, Breather, RunOver }

        /// <summary>The one director in the scene, for HUDs.</summary>
        public static WaveDirector Current { get; private set; }

        [SerializeField] private WaveDefinition[] _waves;
        [SerializeField, Min(1f)] private float _teamMaxHealth = 30f;
        [SerializeField, Min(0f), Tooltip("Seconds after the first player joins before wave 1")] private float _startDelay = 6f;
        [SerializeField, Min(1f), Tooltip("Enemies spawn this far outward from behind a random player")] private float _spawnBehindDistance = 25f;
        [SerializeField, Min(1f), Tooltip("Past the authored list, counts scale by this per wave")] private float _escalation = 1.25f;

        private readonly NetworkVariable<Phase> _phase = new(Phase.Idle, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _wave = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _teamHealth = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<double> _phaseEndTime = new(0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _enemiesAlive = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _kills = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _friendlyFireHits = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private EnemySpawner _spawner;
        private readonly HashSet<Health> _watchedPlayers = new();
        private readonly List<(Enemy prefab, double time)> _spawnQueue = new();
        private int _queueIndex;
        private double _idleSince = -1d;

        public Phase CurrentPhase => _phase.Value;
        public int Wave => _wave.Value;
        public float TeamHealth => _teamHealth.Value;
        public float TeamMaxHealth => _teamMaxHealth;
        public int EnemiesAlive => _enemiesAlive.Value;
        public int Kills => _kills.Value;
        public int FriendlyFireHits => _friendlyFireHits.Value;
        public float PhaseSecondsLeft => Mathf.Max(0f, (float)(_phaseEndTime.Value - NetworkManager.ServerTime.Time));

        private void Awake() => _spawner = GetComponent<EnemySpawner>();
        private void OnEnable() => Current = this;
        private void OnDisable() { if (Current == this) Current = null; }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            _teamHealth.Value = _teamMaxHealth;
            Enemy.AnyDied += OnEnemyDied;
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer) return;
            Enemy.AnyDied -= OnEnemyDied;
            foreach (var h in _watchedPlayers) h.DamageApplied -= OnPlayerDamaged;
            _watchedPlayers.Clear();
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer) return;

            WatchPlayers();
            _enemiesAlive.Value = _spawner.AliveCount;
            double now = NetworkManager.ServerTime.Time;

            switch (_phase.Value)
            {
                case Phase.Idle:
                    if (PlayerRig.Active.Count == 0) { _idleSince = -1d; break; }
                    if (_idleSince < 0d) { _idleSince = now; _phaseEndTime.Value = now + _startDelay; }
                    if (now >= _phaseEndTime.Value) StartWave(_wave.Value + 1);
                    break;

                case Phase.Spawning:
                    while (_queueIndex < _spawnQueue.Count && now >= _spawnQueue[_queueIndex].time)
                    {
                        SpawnBehindSomeone(_spawnQueue[_queueIndex].prefab);
                        _queueIndex++;
                    }
                    if (_queueIndex >= _spawnQueue.Count) _phase.Value = Phase.Fighting;
                    break;

                case Phase.Fighting:
                    if (_spawner.AliveCount == 0)
                    {
                        _phase.Value = Phase.Breather;
                        _phaseEndTime.Value = now + BreatherFor(_wave.Value);
                    }
                    break;

                case Phase.Breather:
                    if (now >= _phaseEndTime.Value) StartWave(_wave.Value + 1);
                    break;

                case Phase.RunOver:
                    if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame) RestartRun();
                    break;
            }
        }

        // ---- Waves ---------------------------------------------------------------------

        private void StartWave(int number)
        {
            if (_waves == null || _waves.Length == 0)
            {
                // Misconfigured scene: say so once and stop, rather than spinning every frame.
                Debug.LogError("[WaveDirector] No waves assigned; run cannot start.", this);
                _phase.Value = Phase.RunOver;
                return;
            }

            _spawnQueue.Clear();
            _queueIndex = 0;
            double now = NetworkManager.ServerTime.Time;

            WaveDefinition def = WaveFor(number, out float scale);
            _wave.Value = number;
            foreach (var entry in def.Entries)
            {
                int count = Mathf.Max(1, Mathf.RoundToInt(entry.Count * scale));
                for (int i = 0; i < count; i++) _spawnQueue.Add((entry.Prefab, now + i * entry.Interval));
            }
            _spawnQueue.Sort((a, b) => a.time.CompareTo(b.time));
            _phase.Value = Phase.Spawning;
        }

        /// <summary>Authored waves in order; past the end, the last one scaled up per extra wave.</summary>
        private WaveDefinition WaveFor(int number, out float scale)
        {
            int index = Mathf.Clamp(number - 1, 0, _waves.Length - 1);
            int overflow = Mathf.Max(0, number - _waves.Length);
            scale = Mathf.Pow(_escalation, overflow);
            return _waves[index];
        }

        private float BreatherFor(int number) => WaveFor(number, out _).BreatherSeconds;

        /// <summary>Behind a random player, outward from the arena centre: in their blind half, in their partner's view.</summary>
        private void SpawnBehindSomeone(Enemy prefab)
        {
            if (PlayerRig.Active.Count == 0) return;
            PlayerRig victim = PlayerRig.Active[Random.Range(0, PlayerRig.Active.Count)];
            Vector3 outward = victim.transform.position;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.forward;
            // Fan them out a little so packs from the same side don't stack.
            outward = Quaternion.Euler(0f, Random.Range(-25f, 25f), 0f) * outward;
            Vector3 position = victim.transform.position + outward * _spawnBehindDistance;
            position.y = 0f;
            _spawner.SpawnPack(prefab, position);
        }

        // ---- Team health & stats --------------------------------------------------------

        private void WatchPlayers()
        {
            foreach (var rig in PlayerRig.Active)
            {
                if (rig.TryGetComponent(out Health h) && _watchedPlayers.Add(h)) h.DamageApplied += OnPlayerDamaged;
            }
        }

        private void OnPlayerDamaged(DamageInfo info, float applied)
        {
            if (_phase.Value == Phase.RunOver || _phase.Value == Phase.Idle) return;

            // Did a player do this? Then it's friendly fire (or a bonk), and it counts.
            foreach (var rig in PlayerRig.Active)
            {
                if (rig.OwnerClientId == info.AttackerClientId && info.AttackerClientId != NetworkManager.ServerClientId)
                {
                    _friendlyFireHits.Value++;
                    break;
                }
            }

            _teamHealth.Value = Mathf.Max(0f, _teamHealth.Value - applied);
            if (_teamHealth.Value <= 0f) EndRun();
        }

        private void OnEnemyDied(Enemy enemy, DamageInfo killingBlow)
        {
            if (_phase.Value is Phase.Spawning or Phase.Fighting) _kills.Value++;
        }

        private void EndRun()
        {
            _phase.Value = Phase.RunOver;
            _spawnQueue.Clear();
            foreach (var enemy in Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            {
                if (enemy.IsSpawned) enemy.NetworkObject.Despawn(destroy: true);
            }
        }

        /// <summary>Server only. Back to wave 0, full health, clean arena.</summary>
        public void RestartRun()
        {
            foreach (var enemy in Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            {
                if (enemy.IsSpawned) enemy.NetworkObject.Despawn(destroy: true);
            }
            foreach (var h in _watchedPlayers) h.ResetToFull();
            _teamHealth.Value = _teamMaxHealth;
            _wave.Value = 0;
            _kills.Value = 0;
            _friendlyFireHits.Value = 0;
            _idleSince = -1d;
            _phase.Value = Phase.Idle;
        }

        /// <summary>Server only. Skip the start delay.</summary>
        public void StartNow()
        {
            if (_phase.Value == Phase.Idle) StartWave(_wave.Value + 1);
        }
    }
}
