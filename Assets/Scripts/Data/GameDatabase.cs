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
    private static Dictionary<string, StatusEffectData> effectsById;
    private static Dictionary<string, AppearancePieceData> appearancePiecesById;
    private static Dictionary<string, AppearanceHeadwearData> appearanceHeadwearById;
    private static Dictionary<string, AppearanceAccessoryData> appearanceAccessoriesById;
    private static List<AbilityData> abilities;
    private static List<ItemData> items;
    private static List<StatusEffectData> effects;
    private static List<AppearancePieceData> appearancePieces;
    private static List<AppearanceHeadwearData> appearanceHeadwear;
    private static List<AppearanceAccessoryData> appearanceAccessories;

    public static IReadOnlyList<AbilityData> Abilities { get { EnsureLoaded(); return abilities; } }
    public static IReadOnlyList<ItemData> Items { get { EnsureLoaded(); return items; } }
    public static IReadOnlyList<StatusEffectData> Effects { get { EnsureLoaded(); return effects; } }
    public static IReadOnlyList<AppearancePieceData> AppearancePieces { get { EnsureLoaded(); return appearancePieces; } }
    public static IReadOnlyList<AppearanceHeadwearData> AppearanceHeadwear { get { EnsureLoaded(); return appearanceHeadwear; } }
    public static IReadOnlyList<AppearanceAccessoryData> AppearanceAccessories { get { EnsureLoaded(); return appearanceAccessories; } }

    public static AbilityData GetAbility(string id) { EnsureLoaded(); return Lookup(abilitiesById, id); }
    public static ItemData GetItem(string id) { EnsureLoaded(); return Lookup(itemsById, id); }
    public static StatusEffectData GetEffect(string id) { EnsureLoaded(); return Lookup(effectsById, id); }
    public static AppearancePieceData GetAppearancePiece(string id) { EnsureLoaded(); return Lookup(appearancePiecesById, id); }
    public static AppearanceHeadwearData GetAppearanceHeadwear(string id) { EnsureLoaded(); return Lookup(appearanceHeadwearById, id); }
    public static AppearanceAccessoryData GetAppearanceAccessory(string id) { EnsureLoaded(); return Lookup(appearanceAccessoriesById, id); }

    private static AppearanceColorPalette palette;

    // Single fixed-path asset, not an Id catalog - see AppearanceColorPalette.
    public static AppearanceColorPalette Palette
    {
        get
        {
            if (palette == null) palette = Resources.Load<AppearanceColorPalette>("Data/AppearanceColorPalette");
            return palette;
        }
    }

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
        effectsById = Index(Resources.LoadAll<StatusEffectData>("Data/Effects"), e => e.Id, out effects);
        appearancePiecesById = Index(Resources.LoadAll<AppearancePieceData>("Data/AppearancePieces"), p => p.Id, out appearancePieces);
        appearanceHeadwearById = Index(Resources.LoadAll<AppearanceHeadwearData>("Data/AppearanceHeadwear"), h => h.Id, out appearanceHeadwear);
        appearanceAccessoriesById = Index(Resources.LoadAll<AppearanceAccessoryData>("Data/AppearanceAccessories"), a => a.Id, out appearanceAccessories);
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
