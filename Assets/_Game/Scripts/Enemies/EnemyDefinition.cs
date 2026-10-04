using SIHKH.Core;
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

        [Header("Movement")]
        [Tooltip("0 = walks on the ground. Higher = flies at this height")] public float FlyHeight = 0f;
        [Min(0f), Tooltip("Sideways weave amplitude, metres. 0 = straight line")] public float WeaveAmplitude = 0f;
        [Min(0.1f), Tooltip("Weaves per second")] public float WeaveFrequency = 1f;

        [Header("Attack")]
        [Min(0.1f), Tooltip("Stops and starts hitting within this distance of its target")]
        public float ReachDistance = 1.8f;
        [Min(0f)] public float AttackDamage = 1f;
        [Min(0.1f), Tooltip("Seconds between hits")] public float AttackInterval = 1f;

        [Header("Spawning")]
        [Min(1), Tooltip("How many appear together")] public int PackSize = 1;

        // Damage multipliers. 1 = normal, below 1 shrugs it off, above 1 is a weak spot.
        // This is what makes a weapon "the" answer to an enemy.
        [Header("Resistances (damage multipliers)")]
        [Range(0f, 3f)] public float KineticMultiplier = 1f; // pea shooter, bouncy
        [Range(0f, 3f)] public float EnergyMultiplier = 1f;  // laser
        [Range(0f, 3f)] public float BluntMultiplier = 1f;   // boomerang, bonks

        public float MultiplierFor(DamageType type) => type switch
        {
            DamageType.Kinetic => KineticMultiplier,
            DamageType.Energy => EnergyMultiplier,
            DamageType.Blunt => BluntMultiplier,
            _ => 1f,
        };
    }
}
