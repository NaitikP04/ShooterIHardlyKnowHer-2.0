using System.Collections.Generic;
using UnityEngine;

namespace SIHKH.Rail
{
    /// <summary>
    /// The seats: one transform per player slot, each already facing the way that
    /// player must look (into the ring, at their partner). The server hands seats out
    /// as clients connect and remembers who is sitting where so the cart physics can
    /// read their input. Plain MonoBehaviour: seat ownership only matters on the
    /// server, and every peer can read the seat transforms from its own scene.
    /// </summary>
    public class CartSeats : MonoBehaviour
    {
        // Exactly one cart pair exists per scene, so a static handle is simpler and safer
        // than every player searching the scene for it.
        public static CartSeats Current { get; private set; }

        [SerializeField] private Transform[] _seats;

        private readonly Dictionary<ulong, int> _seatByClient = new();
        private ICartDriver[] _occupants;

        public int SeatCount => _seats.Length;

        public Transform Seat(int index) => _seats[index];

        /// <summary>
        /// +1 when strafing right from this seat moves the cart in the rail's positive
        /// direction, -1 when it moves it backward along the rail.
        /// </summary>
        public float StrafeRailDirection(int index)
        {
            Transform seat = _seats[index];
            return Vector3.Dot(seat.right, seat.parent.forward) >= 0f ? 1f : -1f;
        }

        private void Awake() => _occupants = new ICartDriver[_seats.Length];

        private void OnEnable() => Current = this;

        private void OnDisable()
        {
            if (Current == this) Current = null;
        }

        /// <summary>Server only. Returns the lowest free seat, or -1 when both are taken.</summary>
        public int Claim(ulong clientId, ICartDriver driver)
        {
            if (_seatByClient.TryGetValue(clientId, out int existing)) return existing;

            for (int i = 0; i < _seats.Length; i++)
            {
                if (!_seatByClient.ContainsValue(i))
                {
                    _seatByClient[clientId] = i;
                    _occupants[i] = driver;
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Server only.</summary>
        public void Release(ulong clientId)
        {
            if (_seatByClient.Remove(clientId, out int seat))
            {
                _occupants[seat] = null;
            }
        }

        /// <summary>Server only. An empty seat holds still.</summary>
        public float Strafe(int seat) => _occupants[seat]?.Strafe ?? 0f;

        /// <summary>Server only. An empty seat is never planted, so it can be dragged.</summary>
        public bool Planted(int seat) => _occupants[seat]?.Planted ?? false;
    }
}
