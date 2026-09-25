using UnityEngine;

namespace SIHKH.Rail
{
    /// <summary>
    /// Draws the chain straight across the ring between the two carts. Purely cosmetic
    /// and purely local: the anchors it reads are already where the network says they
    /// are. Droops when slack, pulls taut as it stretches, and vanishes when snapped.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class ChainVisual : MonoBehaviour
    {
        [SerializeField] private CartPair _carts;
        [SerializeField] private float _slackSag = 0.8f;   // droop in the middle when fully slack, metres
        [SerializeField, Min(2)] private int _segments = 12;

        private LineRenderer _line;

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.positionCount = _segments + 1;
        }

        private void LateUpdate()
        {
            _line.enabled = !_carts.Snapped;
            if (!_line.enabled) return;

            // Directly across = full sag; at the snap point the chain is a straight line.
            float tension = Mathf.InverseLerp(0f, _carts.SnapStretch, Mathf.Abs(_carts.Stretch));
            float sag = _slackSag * (1f - tension);

            Vector3 a = _carts.CartA.ChainAnchor.position;
            Vector3 b = _carts.CartB.ChainAnchor.position;

            for (int i = 0; i <= _segments; i++)
            {
                float t = i / (float)_segments;
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y -= sag * 4f * t * (1f - t); // parabola: 0 at the ends, sag in the middle
                _line.SetPosition(i, p);
            }
        }
    }
}
