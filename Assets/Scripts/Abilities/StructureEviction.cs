using System;
using System.Collections.Generic;

// Pure bookkeeping for a caster's placed structures (Summon Wall keeps up to
// AbilityData.MaxActiveStructures). Kept free of NetworkObject so the cycle
// rule is unit-testable (Assets/Tests/EditMode/StructureEvictionTests.cs).
public static class StructureEviction
{
    // `active` is oldest first. Removes every dead entry (without counting it
    // toward the cap - a wall that already despawned must not push a live one
    // out), then drops the oldest live entries until there is room for one
    // more. Returns the evicted live entries, oldest first; the caller
    // despawns them and then appends the new structure. max < 1 acts as 1.
    public static List<T> MakeRoom<T>(List<T> active, int max, Predicate<T> isDead)
    {
        List<T> evicted = new List<T>();
        if (active == null) return evicted;

        if (isDead != null) active.RemoveAll(isDead);

        int cap = Math.Max(1, max);
        while (active.Count >= cap)
        {
            evicted.Add(active[0]);
            active.RemoveAt(0);
        }
        return evicted;
    }
}
