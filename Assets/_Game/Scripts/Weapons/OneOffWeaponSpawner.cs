using SIHKH.Rail;
using Unity.Netcode;
using UnityEngine;

namespace SIHKH.Weapons
{
    /// <summary>
    /// Whitebox: put one one-off weapon on the floor next to a seat when the session starts,
    /// so the throw/catch loop can be tried without enemy drops existing yet.
    /// </summary>
    public class OneOffWeaponSpawner : NetworkBehaviour
    {
        [SerializeField] private OneOffWeapon _prefab;
        [SerializeField, Min(0)] private int _seat = 0;
        [SerializeField] private Vector3 _offsetFromSeat = new(0f, 0f, 0.8f);

        private bool _spawned;

        private void Update()
        {
            if (!IsSpawned || !IsServer || _spawned || CartSeats.Current == null) return;

            // The seats sit at the origin until CartPair's first tick puts the carts on the
            // rail; spawning before that drops the weapon in the middle of nowhere.
            if (CartSeats.Current.TryGetComponent(out CartPair pair) && !pair.Placed) return;
            _spawned = true;

            Transform seat = CartSeats.Current.Seat(_seat);
            Vector3 position = seat.TransformPoint(_offsetFromSeat);
            OneOffWeapon item = Instantiate(_prefab, position, Quaternion.identity);
            item.NetworkObject.Spawn(destroyWithScene: true);
            item.Drop(position);
        }

        public override void OnNetworkDespawn() => _spawned = false;
    }
}
