using System.Collections.Generic;
using UnityEngine;

namespace SIHKH.UI
{
    /// <summary>
    /// Floating damage numbers. Spawned from hit events on every peer, drawn by the local
    /// HUD in screen space (projected from the world hit point), so they render regardless
    /// of pipeline. Colour says how the hit was received: grey = resisted, white = normal,
    /// gold and bigger = weak spot. Whitebox; the real UI pass replaces the drawing.
    /// </summary>
    public static class DamageNumber
    {
        public struct Entry
        {
            public Vector3 WorldPosition;
            public string Text;
            public Color Color;
            public float Scale;
            public float Age;
        }

        public const float Lifetime = 0.8f;
        private const float RiseSpeed = 1.2f;

        private static readonly List<Entry> _entries = new();

        public static IReadOnlyList<Entry> Entries => _entries;

        public static void Spawn(Vector3 point, float amount, float multiplier)
        {
            bool resisted = multiplier < 0.95f;
            bool weakSpot = multiplier > 1.05f;
            _entries.Add(new Entry
            {
                WorldPosition = point + Vector3.up * 0.3f + Random.insideUnitSphere * 0.15f,
                Text = weakSpot ? $"{amount:0.#}!" : $"{amount:0.#}",
                Color = resisted ? new Color(0.65f, 0.65f, 0.65f) : weakSpot ? new Color(1f, 0.85f, 0.2f) : Color.white,
                Scale = weakSpot ? 1.5f : resisted ? 0.8f : 1f,
                Age = 0f,
            });
        }

        /// <summary>Call once per frame from whoever draws them.</summary>
        public static void Tick(float deltaTime)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry e = _entries[i];
                e.Age += deltaTime;
                e.WorldPosition += Vector3.up * (RiseSpeed * deltaTime);
                if (e.Age >= Lifetime) _entries.RemoveAt(i);
                else _entries[i] = e;
            }
        }
    }
}
