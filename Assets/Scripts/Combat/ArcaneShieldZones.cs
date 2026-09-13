using System.Collections.Generic;
using UnityEngine;

// Server-only shared registry of active Arcane Shield domes (Skeleton
// Mage). Not a NetworkObject/prefab - the dome has no physical presence in
// the world, it's purely a "ranged attacks can't target anyone standing
// here" rule checked at cast time (PlayerAutoAttack, PlayerAbilities), the
// same way an out-of-range or facing-cone check already gates a cast. One
// static list (one server process) rather than per-instance, mirroring
// CharacterEquipment.globalItemOwners.
public static class ArcaneShieldZones
{
    private struct Zone
    {
        public Vector3 Position;
        public float Radius;
        public float ExpireTime;
    }

    private static readonly List<Zone> zones = new List<Zone>();

    public static void Add(Vector3 position, float radius, float duration)
    {
        zones.Add(new Zone { Position = position, Radius = radius, ExpireTime = Time.time + duration });
    }

    // True if a ranged attack/ability may not target this position right
    // now. Expired zones are pruned lazily here rather than on a separate
    // tick, since this is the only place they're ever read.
    public static bool Blocks(Vector3 targetPosition)
    {
        for (int i = zones.Count - 1; i >= 0; i--)
        {
            if (Time.time >= zones[i].ExpireTime)
            {
                zones.RemoveAt(i);
                continue;
            }
            if (Vector3.Distance(zones[i].Position, targetPosition) <= zones[i].Radius) return true;
        }
        return false;
    }
}
