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

        [Header("Tension readout")]
        // Slack -> pulling -> about to snap. Vertex colours, so no material swap needed.
        [SerializeField] private Gradient _tensionColor = DefaultGradient();
        [SerializeField] private float _slackWidth = 0.12f;
        [SerializeField] private float _tautWidth = 0.3f;

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

            Color c = _tensionColor.Evaluate(tension);
            _line.startColor = _line.endColor = c;
            _line.startWidth = _line.endWidth = Mathf.Lerp(_slackWidth, _tautWidth, tension);

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

        private static Gradient DefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.3f, 0.85f, 0.35f), 0f),   // slack: green
                    new GradientColorKey(new Color(0.95f, 0.85f, 0.2f), 0.5f), // pulling: yellow
                    new GradientColorKey(new Color(0.95f, 0.2f, 0.15f), 1f),   // snapping: red
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
