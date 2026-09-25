using UnityEngine;

namespace SIHKH.Rail
{
    /// <summary>
    /// Draws the chain between the two carts. Purely cosmetic and purely local: the
    /// anchors it reads are already where the network says they are.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class ChainVisual : MonoBehaviour
    {
        [SerializeField] private RailTrain _train;
        [SerializeField] private float _sag = 0.6f;      // how far the middle droops, metres
        [SerializeField, Min(2)] private int _segments = 12;

        private LineRenderer _line;

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.positionCount = _segments + 1;
        }

        private void LateUpdate()
        {
            Vector3 a = _train.LeadCart.ChainAnchor.position;
            Vector3 b = _train.RearCart.ChainAnchor.position;

            for (int i = 0; i <= _segments; i++)
            {
                float t = i / (float)_segments;
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y -= _sag * 4f * t * (1f - t); // parabola: 0 at the ends, _sag in the middle
                _line.SetPosition(i, p);
            }
        }
    }
}
