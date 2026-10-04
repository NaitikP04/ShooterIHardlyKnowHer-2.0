using System;
using SIHKH.Enemies;
using UnityEngine;

namespace SIHKH.Waves
{
    /// <summary>One wave, as data: what spawns, how fast, and how long the breather after it is.</summary>
    [CreateAssetMenu(menuName = "SIHKH/Wave Definition", fileName = "Wave")]
    public class WaveDefinition : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public Enemy Prefab;
            [Min(1), Tooltip("Packs of this type (a pack is the enemy's PackSize)")] public int Count;
            [Min(0f), Tooltip("Seconds between packs of this entry")] public float Interval;
        }

        public string DisplayName = "Wave";
        public Entry[] Entries;
        [Min(0f), Tooltip("Seconds of calm after the last enemy dies")] public float BreatherSeconds = 8f;
    }
}
