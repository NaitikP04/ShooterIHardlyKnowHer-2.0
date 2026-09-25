using System.Collections.Generic;
using UnityEngine;

namespace SIHKH.Rail
{
    /// <summary>
    /// The cart's seats: one transform per player slot, each already facing the way
    /// that player must look. The server hands seats out as clients connect. Plain
    /// MonoBehaviour: seat ownership only matters on the server, and every peer can
    /// read the seat transforms from its own copy of the scene.
    /// </summary>
    public class CartSeats : MonoBehaviour
    {
        // Exactly one cart exists per scene, so a static handle is simpler and safer than
        // every player searching the scene for it.
        public static CartSeats Current { get; private set; }

        [SerializeField] private Transform[] _seats;

        private readonly Dictionary<ulong, int> _seatByClient = new();

        public int SeatCount => _seats.Length;

        public Transform Seat(int index) => _seats[index];

        private void OnEnable() => Current = this;

        private void OnDisable()
        {
            if (Current == this) Current = null;
        }

        /// <summary>Server only. Returns the lowest free seat, or -1 when the cart is full.</summary>
        public int Claim(ulong clientId)
        {
            if (_seatByClient.TryGetValue(clientId, out int existing)) return existing;

            for (int i = 0; i < _seats.Length; i++)
            {
                if (!_seatByClient.ContainsValue(i))
                {
                    _seatByClient[clientId] = i;
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Server only.</summary>
        public void Release(ulong clientId) => _seatByClient.Remove(clientId);
    }
}
