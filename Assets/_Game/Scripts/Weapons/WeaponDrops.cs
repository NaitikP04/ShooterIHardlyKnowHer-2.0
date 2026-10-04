using SIHKH.Enemies;
using UnityEngine;

namespace SIHKH.Weapons
{
    /// <summary>
    /// Server only. Enemies drop the one-off that counters them, where they die, which by
    /// design is usually behind the player who couldn't see them. Every one-off is a single
    /// shared instance, so a drop only happens if that weapon isn't in the world yet.
    /// </summary>
    public static class WeaponDrops
    {
        public static bool TryDrop(EnemyDefinition enemy, Vector3 position)
        {
            if (enemy.DropPrefab == null) return false;
            if (Random.value > enemy.DropChance) return false;

            foreach (var existing in OneOffWeapon.All)
            {
                if (existing.Definition == enemy.DropPrefab.Definition) return false; // already out there
            }

            OneOffWeapon item = Object.Instantiate(enemy.DropPrefab, position, Quaternion.identity);
            item.NetworkObject.Spawn(destroyWithScene: true);
            item.Drop(position);
            return true;
        }
    }
}
