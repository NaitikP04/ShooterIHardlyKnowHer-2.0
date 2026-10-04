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

        [Header("Thrown attack (boomerang-style)")]
        [Tooltip("Firing throws the weapon itself along an out-and-back path instead of shooting")]
        public bool ThrowToAttack = false;
        [Range(0f, 1f), Tooltip("Sideways curve as a fraction of Range")] public float BoomerangCurve = 0.3f;
        [Min(0.5f), Tooltip("Seconds for the full out-and-back trip")] public float BoomerangSeconds = 2.4f;

        [Header("Projectile (leave prefab empty for hitscan)")]
        public Projectile ProjectilePrefab;
        [Min(1f)] public float ProjectileSpeed = 22f;
        [Min(0), Tooltip("Ricochets before it dies. 0 = dies on first contact")] public int Bounces = 0;
        [Range(0f, 1f), Tooltip("Speed kept after each bounce")] public float Restitution = 0.9f;
        [Range(0f, 2f)] public float GravityScale = 1f;

        [Header("Presentation")]
        public Color TracerColor = new(1f, 0.9f, 0.3f);

        public bool IsHitscan => ProjectilePrefab == null;
        public float SecondsBetweenShots => 1f / FireRate;
    }
}
