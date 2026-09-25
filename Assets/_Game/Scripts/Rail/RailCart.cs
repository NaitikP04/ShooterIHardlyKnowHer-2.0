using Unity.Netcode;
using UnityEngine;
using UnityEngine.Splines;

namespace SIHKH.Rail
{
    /// <summary>
    /// The cart both players ride. The server owns a single number, distance travelled
    /// along the rail; every machine turns that number into a position on its own copy
    /// of the spline. Nothing else about the cart is ever sent over the network.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class RailCart : NetworkBehaviour
    {
        [SerializeField] private SplineContainer _rail;
        [SerializeField, Min(0f)] private float _speed = 4f; // metres per second
        [SerializeField] private bool _loop = true;

        // Server writes, everyone reads.
        private readonly NetworkVariable<float> _distance = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private float _railLength;

        public float Speed => _speed;
        public float Distance => _distance.Value;

        private void Awake()
        {
            _railLength = _rail.CalculateLength();
        }

        private void Update()
        {
            if (IsServer)
            {
                float d = _distance.Value + _speed * Time.deltaTime;
                _distance.Value = _loop ? d % _railLength : Mathf.Min(d, _railLength);
            }

            PlaceOnRail(_distance.Value);
        }

        private void PlaceOnRail(float distance)
        {
            // Splines are sampled by a 0..1 parameter, not by metres, so normalise first.
            float t = Mathf.Clamp01(distance / _railLength);
            _rail.Evaluate(t, out var position, out var tangent, out var up);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(tangent, up));
        }
    }
}
