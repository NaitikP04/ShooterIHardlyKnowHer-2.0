using UnityEngine;

namespace SIHKH.Core
{
    /// <summary>
    /// What kind of hurt. Enemies will resist or crumble to specific types; that's how a
    /// weapon becomes "the" answer to an enemy.
    /// </summary>
    public enum DamageType
    {
        Kinetic, // pea shooter, bouncy
        Energy,  // laser
        Blunt,   // boomerang, getting bonked by a badly caught weapon, enemy melee
    }

    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly DamageType Type;
        public readonly Vector3 Point;
        public readonly Vector3 Direction;
        public readonly ulong AttackerClientId; // who to blame; also who to credit

        public DamageInfo(float amount, DamageType type, Vector3 point, Vector3 direction, ulong attackerClientId)
        {
            Amount = amount;
            Type = type;
            Point = point;
            Direction = direction;
            AttackerClientId = attackerClientId;
        }
    }

    /// <summary>Anything a weapon can hurt. Only ever called on the server.</summary>
    public interface IDamageable
    {
        void TakeDamage(in DamageInfo info);
    }
}
