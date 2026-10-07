using UnityEditor;
using UnityEngine;

// One-off batch tool: wires 3D weapon/shield model prefabs (from the
// imported Blink pack's used subset, Assets/External/Blink/...) and their
// category's WeaponAttachProfile onto the matching ItemData assets - a menu
// command instead of manual Inspector drags. See CharacterWeaponVisual for
// what reads these fields at runtime. Lives alongside IconWiringTool under
// Assets/Scripts/Editor (Editor-only via Encounter.Editor.asmdef - see
// IconWiringTool's header for why the folder name alone isn't enough).
public static class WeaponModelWiringTool
{
    private readonly struct Entry
    {
        public readonly string AssetPath;
        public readonly string PrefabPath;
        public readonly string ProfilePath;

        public Entry(string assetPath, string prefabPath, string profilePath)
        {
            AssetPath = assetPath;
            PrefabPath = prefabPath;
            ProfilePath = profilePath;
        }
    }

    private const string ItemRoot = "Assets/Resources/Data/Items/";
    private const string BlinkRoot = "Assets/External/Blink/Art/Weapons/";
    private const string ProfileRoot = "Assets/Data/WeaponAttachProfiles/";

    private static readonly Entry[] Entries =
    {
        new Entry(ItemRoot + "GearBroadSword.asset", BlinkRoot + "Stylized/Swords/_PrefabsSwords/Sword1_1_1.prefab", ProfileRoot + "AttachProfileSword.asset"),
        new Entry(ItemRoot + "GearStaff.asset", BlinkRoot + "Stylized/Staves/_PrefabsStaves/Staff2_1_2.prefab", ProfileRoot + "AttachProfileStaff.asset"),
        new Entry(ItemRoot + "GearTwoHandedAxe.asset", BlinkRoot + "Stylized/Axes/PrefabsAxes/AxeEvolving2_3_1.prefab", ProfileRoot + "AttachProfileAxe2H.asset"),
        new Entry(ItemRoot + "GearHuntersBow.asset", BlinkRoot + "Stylized/Bows/_PrefabsBows/Bow2_3_1.prefab", ProfileRoot + "AttachProfileBow.asset"),
        new Entry(ItemRoot + "GearArmorbreaker.asset", BlinkRoot + "Stylized/Maces/_PrefabsMaces/Mace3_1_1.prefab", ProfileRoot + "AttachProfileMace.asset"),
        new Entry(ItemRoot + "GearAegisOfTheUnstoppable.asset", BlinkRoot + "Stylized/Shields/_PrefabsShields/Shield1_1_3.prefab", ProfileRoot + "AttachProfileShield.asset"),
        new Entry(ItemRoot + "GearShieldOfTheMagi.asset", BlinkRoot + "Stylized/Shields/_PrefabsShields/Shield2_2_2.prefab", ProfileRoot + "AttachProfileShield.asset"),
        new Entry(ItemRoot + "GearHolyScepter.asset", BlinkRoot + "LowPoly/FreeRPGWeapons/_PREFABS/Wand_Epic.prefab", ProfileRoot + "AttachProfileWand.asset"),
    };

    [MenuItem("Encounter/Wire Weapon Models")]
    private static void WireWeaponModels()
    {
        int wired = 0;
        int failed = 0;

        foreach (Entry entry in Entries)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.PrefabPath);
            WeaponAttachProfile profile = AssetDatabase.LoadAssetAtPath<WeaponAttachProfile>(entry.ProfilePath);
            ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(entry.AssetPath);
            if (prefab == null || profile == null || item == null)
            {
                Debug.LogWarning($"[WeaponModelWiringTool] Skipped {entry.AssetPath} - " +
                    $"prefab {(prefab != null ? "ok" : "MISSING")}, profile {(profile != null ? "ok" : "MISSING")}, item {(item != null ? "ok" : "MISSING")}.");
                failed++;
                continue;
            }

            item.WeaponModelPrefab = prefab;
            item.AttachProfile = profile;
            EditorUtility.SetDirty(item);
            wired++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[WeaponModelWiringTool] Wired {wired} weapon model(s), {failed} failed/skipped. " +
            "Alignment is tuned per profile, not per item - see Encounter/Weapon Attach Tuner.");
    }
}
