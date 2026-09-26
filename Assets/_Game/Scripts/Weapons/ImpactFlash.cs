using UnityEngine;

namespace SIHKH.Weapons
{
    /// <summary>
    /// A small sphere that pops at the hit point and shrinks away. Local cosmetic only.
    /// </summary>
    public class ImpactFlash : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float _lifetime = 0.25f;
        [SerializeField] private float _startScale = 0.5f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private float _age;

        public void Show(Vector3 point, Color color)
        {
            transform.position = point;
            transform.localScale = Vector3.one * _startScale;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, color);
            GetComponent<Renderer>().SetPropertyBlock(block);
            _age = 0f;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= _lifetime)
            {
                Destroy(gameObject);
                return;
            }
            transform.localScale = Vector3.one * (_startScale * (1f - _age / _lifetime));
        }
    }
}
