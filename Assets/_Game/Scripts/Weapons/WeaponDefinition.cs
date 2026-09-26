using SIHKH.Core;
using UnityEngine;

namespace SIHKH.Weapons
{
    /// <summary>
    /// Everything tunable about a gun, as data. Behaviour lives in components; numbers live
    /// here so they can be tweaked in the Inspector without touching code.
    /// </summary>
    [CreateAssetMenu(menuName = "SIHKH/Weapon Definition", fileName = "Weapon")]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string DisplayName = "Pea Shooter";

        [Header("Firing")]
        [Min(0.1f), Tooltip("Shots per second")] public float FireRate = 2f;
        [Min(0f)] public float Damage = 4f;
        public DamageType DamageType = DamageType.Kinetic;
        [Min(1f), Tooltip("Metres a hitscan shot travels")] public float Range = 60f;
        [Range(0f, 10f), Tooltip("Cone half-angle of random spread, degrees")] public float SpreadDegrees = 0.5f;

        [Header("Presentation")]
        public Color TracerColor = new(1f, 0.9f, 0.3f);

        public float SecondsBetweenShots => 1f / FireRate;
    }
}
