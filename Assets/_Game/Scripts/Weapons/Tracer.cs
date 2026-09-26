using UnityEngine;

namespace SIHKH.Weapons
{
    /// <summary>
    /// A brief line from muzzle to impact. Local cosmetic only; spawned on every peer by
    /// the shot RPC. Instantiate/Destroy is fine at whitebox fire rates; pool it later.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class Tracer : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float _lifetime = 0.25f;

        private LineRenderer _line;
        private Color _color;
        private float _age;

        public void Show(Vector3 from, Vector3 to, Color color)
        {
            _line = GetComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.SetPosition(0, from);
            _line.SetPosition(1, to);
            _color = color;
            _age = 0f;
            Apply(1f);
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= _lifetime)
            {
                Destroy(gameObject);
                return;
            }
            Apply(1f - _age / _lifetime);
        }

        private void Apply(float alpha)
        {
            Color c = _color;
            c.a = alpha;
            _line.startColor = _line.endColor = c;
        }
    }
}
