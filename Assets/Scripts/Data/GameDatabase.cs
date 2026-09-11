using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Registry of every data asset the game knows about, discovered from
// Resources/Data on first access. Stable string Ids are what cross the
// network and what saved profiles store, so reordering, renaming or adding
// assets never silently rebinds anything.
public static class GameDatabase
{
    private static Dictionary<string, AbilityData> abilitiesById;
    private static Dictionary<string, ItemData> itemsById;
    private static List<AbilityData> abilities;
    private static List<ItemData> items;

    public static IReadOnlyList<AbilityData> Abilities { get { EnsureLoaded(); return abilities; } }
    public static IReadOnlyList<ItemData> Items { get { EnsureLoaded(); return items; } }

    public static AbilityData GetAbility(string id) => Lookup(Abilities == null ? null : abilitiesById, id);
    public static ItemData GetItem(string id) => Lookup(Items == null ? null : itemsById, id);

    private static T Lookup<T>(Dictionary<string, T> table, string id) where T : class
    {
        if (table == null || string.IsNullOrEmpty(id)) return null;
        return table.TryGetValue(id, out T value) ? value : null;
    }

    private static void EnsureLoaded()
    {
        if (abilitiesById != null) return;
        abilitiesById = Index(Resources.LoadAll<AbilityData>("Data/Abilities"), a => a.Id, out abilities);
        itemsById = Index(Resources.LoadAll<ItemData>("Data/Items"), i => i.Id, out items);
    }

    private static Dictionary<string, T> Index<T>(T[] loaded, Func<T, string> idOf, out List<T> ordered) where T : UnityEngine.Object
    {
        ordered = loaded.OrderBy(a => a.name, StringComparer.Ordinal).ToList();
        Dictionary<string, T> table = new Dictionary<string, T>();
        foreach (T asset in ordered)
        {
            string id = idOf(asset);
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogError($"[GameDatabase] {asset.name} has no Id set; it will be unreachable.");
                continue;
            }
            if (table.ContainsKey(id))
            {
                Debug.LogError($"[GameDatabase] Duplicate Id '{id}' on {asset.name} and {table[id].name}.");
                continue;
            }
            table[id] = asset;
        }
        return table;
    }
}
