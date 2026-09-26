using System.Collections.Generic;
using SIHKH.Enemies;
using Unity.Netcode;
using UnityEngine;

namespace SIHKH.Waves
{
    /// <summary>
    /// Whitebox stand-in for the wave director: drips enemies onto a ring around the arena
    /// on a timer, capped. Server only, like everything that creates gameplay objects.
    /// </summary>
    public class EnemySpawner : NetworkBehaviour
    {
        [SerializeField] private Enemy _enemyPrefab;
        [SerializeField, Min(0.1f)] private float _interval = 3f;
        [SerializeField, Min(1)] private int _maxAlive = 8;
        [SerializeField, Min(1f)] private float _spawnRadius = 35f;
        [SerializeField] private bool _autoSpawn = true;

        private readonly List<Enemy> _alive = new();
        private float _nextSpawnTime;

        public int AliveCount
        {
            get
            {
                _alive.RemoveAll(e => e == null || !e.IsSpawned);
                return _alive.Count;
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || !_autoSpawn) return;
            if (Time.time < _nextSpawnTime) return;

            _nextSpawnTime = Time.time + _interval;
            if (AliveCount < _maxAlive) SpawnOne();
        }

        /// <summary>Server only.</summary>
        public Enemy SpawnOne()
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector3 position = new(Mathf.Cos(angle) * _spawnRadius, 0.6f, Mathf.Sin(angle) * _spawnRadius);

            Enemy enemy = Instantiate(_enemyPrefab, position, Quaternion.LookRotation(-position));
            enemy.NetworkObject.Spawn(destroyWithScene: true);
            _alive.Add(enemy);
            return enemy;
        }
    }
}
