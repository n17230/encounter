using System.Collections.Generic;

// Live list of every currently-enabled instance of T, maintained by the
// instances themselves (Add in OnEnable, Remove in OnDisable) - replaces
// FindObjectsByType<T>() scene scans, which were running per cast, per mob
// per physics tick, and once per frame for the HUD. Same membership rule a
// scan has (active + enabled objects only), without the scan.
//
// All is the live list: don't hold it across anything that can
// enable/disable a T mid-iteration. Code that applies hits while iterating
// (a hit can kill, a death can despawn) should iterate Snapshot() instead.
public static class Registry<T> where T : class
{
    private static readonly List<T> items = new List<T>();
    private static readonly List<T> snapshot = new List<T>();

    public static IReadOnlyList<T> All => items;

    public static void Add(T item)
    {
        if (item != null && !items.Contains(item)) items.Add(item);
    }

    public static void Remove(T item)
    {
        items.Remove(item);
    }

    // A reused scratch copy - valid until the next Snapshot() call for the
    // same T, so don't nest two snapshot loops over the same type.
    public static IReadOnlyList<T> Snapshot()
    {
        snapshot.Clear();
        snapshot.AddRange(items);
        return snapshot;
    }
}
