using SIHKH.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SIHKH.Waves
{
    /// <summary>
    /// Sandbox only: the host presses F1..F4 to drop one pack of each enemy type 15 m in
    /// front of their own view. Server-side by construction (only the host has the keys),
    /// so no RPC is needed.
    /// </summary>
    [RequireComponent(typeof(EnemySpawner))]
    public class SandboxSpawnKeys : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float _distance = 15f;

        private EnemySpawner _spawner;

        private void Awake() => _spawner = GetComponent<EnemySpawner>();

        private void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || Keyboard.current == null) return;

            Key[] keys = { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6 };
            for (int i = 0; i < keys.Length && i < _spawner.Prefabs.Count; i++)
            {
                if (Keyboard.current[keys[i]].wasPressedThisFrame) SpawnInFront(i);
            }
        }

        private void SpawnInFront(int index)
        {
            PlayerRig host = null;
            foreach (var rig in PlayerRig.Active)
            {
                if (rig.IsOwner) { host = rig; break; }
            }
            if (host == null) return;

            Vector3 forward = host.Head.forward;
            forward.y = 0f;
            forward.Normalize();
            _spawner.SpawnPack(_spawner.Prefabs[index], host.transform.position + forward * _distance);
        }
    }
}
