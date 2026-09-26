using UnityEngine;

namespace SIHKH.Enemies
{
    /// <summary>Everything tunable about an enemy type, as data.</summary>
    [CreateAssetMenu(menuName = "SIHKH/Enemy Definition", fileName = "Enemy")]
    public class EnemyDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string DisplayName = "Grunt";
        public Color Tint = new(0.6f, 0.2f, 0.2f);

        [Header("Stats")]
        [Min(1f)] public float MaxHealth = 10f;
        [Min(0f), Tooltip("Metres per second toward its target")] public float MoveSpeed = 2f;

        [Header("Attack")]
        [Min(0.1f), Tooltip("Stops and starts hitting within this distance of its target")]
        public float ReachDistance = 1.8f;
        [Min(0f)] public float AttackDamage = 1f;
        [Min(0.1f), Tooltip("Seconds between hits")] public float AttackInterval = 1f;
    }
}
