using UnityEngine;

namespace SIHKH.UI
{
    /// <summary>
    /// A floating number that pops at a hit point, drifts up and fades. Colour says how the
    /// hit was received: grey = resisted, white = normal, yellow and bigger = weak spot.
    /// Local cosmetic only; whitebox TextMesh until the real UI pass.
    /// </summary>
    public class DamageNumber : MonoBehaviour
    {
        private const float Lifetime = 0.8f;
        private const float RiseSpeed = 1.4f;

        private TextMesh _text;
        private float _age;
        private Color _color;

        public static void Spawn(Vector3 point, float amount, float multiplier)
        {
            var go = new GameObject("DamageNumber");
            go.transform.position = point + Vector3.up * 0.4f + Random.insideUnitSphere * 0.15f;
            var number = go.AddComponent<DamageNumber>();
            number.Setup(amount, multiplier);
        }

        private void Setup(float amount, float multiplier)
        {
            _text = gameObject.AddComponent<TextMesh>();
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.fontSize = 48;
            _text.characterSize = multiplier > 1.05f ? 0.14f : 0.09f;
            _text.text = multiplier > 1.05f ? $"{amount:0.#}!" : $"{amount:0.#}";

            _color = multiplier < 0.95f ? new Color(0.6f, 0.6f, 0.6f)
                   : multiplier > 1.05f ? new Color(1f, 0.9f, 0.2f)
                   : Color.white;
            _text.color = _color;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= Lifetime)
            {
                Destroy(gameObject);
                return;
            }

            transform.position += Vector3.up * (RiseSpeed * Time.deltaTime);

            // Always face whichever camera is rendering this peer's view.
            Camera cam = Camera.current != null ? Camera.current : Camera.main;
            if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);

            Color c = _color;
            c.a = 1f - Mathf.Clamp01((_age - Lifetime * 0.5f) / (Lifetime * 0.5f));
            _text.color = c;
        }
    }
}
