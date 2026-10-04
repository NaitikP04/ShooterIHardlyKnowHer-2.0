using System.Collections.Generic;
using SIHKH.Enemies;
using Unity.Netcode;
using UnityEngine;

namespace SIHKH.Waves
{
    /// <summary>
    /// Whitebox stand-in for the wave director: drips enemies onto a ring around the arena
    /// on a timer, capped, picking a type at random. Server only, like everything that
    /// creates gameplay objects. Packs (definition.PackSize) spawn clustered.
    /// </summary>
    public class EnemySpawner : NetworkBehaviour
    {
        [SerializeField] private Enemy[] _enemyPrefabs;
        [SerializeField, Min(0.1f)] private float _interval = 3f;
        [SerializeField, Min(1)] private int _maxAlive = 8;
        [SerializeField, Min(1f)] private float _spawnRadius = 35f;
        [SerializeField] private bool _autoSpawn = true;

        private readonly List<Enemy> _alive = new();
        private float _nextSpawnTime;

        public IReadOnlyList<Enemy> Prefabs => _enemyPrefabs;

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
            if (!IsSpawned || !IsServer || !_autoSpawn || _enemyPrefabs.Length == 0) return;
            if (Time.time < _nextSpawnTime) return;

            _nextSpawnTime = Time.time + _interval;
            if (AliveCount < _maxAlive) SpawnOne();
        }

        /// <summary>Server only. A random type at a random point on the spawn ring.</summary>
        public Enemy SpawnOne()
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector3 position = new(Mathf.Cos(angle) * _spawnRadius, 0f, Mathf.Sin(angle) * _spawnRadius);
            return SpawnPack(_enemyPrefabs[Random.Range(0, _enemyPrefabs.Length)], position);
        }

        /// <summary>Server only. Spawns the type's whole pack around <paramref name="position"/>; returns the first.</summary>
        public Enemy SpawnPack(Enemy prefab, Vector3 position)
        {
            Enemy first = null;
            int count = Mathf.Max(1, prefab.Definition.PackSize);
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = count > 1 ? Quaternion.Euler(0f, i * 360f / count, 0f) * Vector3.forward * 1.5f : Vector3.zero;
                Enemy enemy = Instantiate(prefab, position + offset, Quaternion.LookRotation(-position.normalized));
                enemy.NetworkObject.Spawn(destroyWithScene: true);
                _alive.Add(enemy);
                first ??= enemy;
            }
            return first;
        }
    }
}
