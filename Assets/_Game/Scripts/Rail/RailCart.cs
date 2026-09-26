using Unity.Netcode;
using UnityEngine;
using UnityEngine.Splines;

namespace SIHKH.Rail
{
    /// <summary>
    /// One cart on the rail. Its only replicated state is a distance along the spline,
    /// written by the server; every machine turns that number into a pose on its own
    /// copy of the spline. The cart itself never decides to move: RailTrain drives it.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class RailCart : NetworkBehaviour
    {
        [SerializeField] private SplineContainer _rail;
        [SerializeField] private Transform _seat;
        [SerializeField] private Transform _chainAnchor;

        private readonly NetworkVariable<float> _distance = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private float _railLength;

        public Transform Seat => _seat;
        public Transform ChainAnchor => _chainAnchor;
        public float Distance => _distance.Value;
        public float RailLength => _railLength;

        private void Awake()
        {
            _railLength = _rail.CalculateLength();
        }

        /// <summary>Server only. Distances wrap, so the loop is endless.</summary>
        public void SetDistance(float metres)
        {
            _distance.Value = Mathf.Repeat(metres, _railLength);
            // Move now, not next Update: anything reading the seat this frame (spawners,
            // the players' LateUpdate) must see the cart where its distance says it is.
            PlaceOnRail(_distance.Value);
        }

        /// <summary>Server only.</summary>
        public void Advance(float metres) => SetDistance(_distance.Value + metres);

        private void Update()
        {
            // IsSpawned, not IsServer: NGO never clears IsServer on shutdown.
            if (!IsSpawned) return;
            PlaceOnRail(_distance.Value);
        }

        private void PlaceOnRail(float distance)
        {
            _rail.Evaluate(distance / _railLength, out var position, out var tangent, out var up);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(tangent, up));
        }
    }
}
