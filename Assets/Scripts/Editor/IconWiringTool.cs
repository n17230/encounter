using UnityEditor;
using UnityEngine;

// One-off batch tool: wires Sprite icons (from the imported 6000FantasyIcons
// pack's used subset, Assets/External/6000FantasyIcons/...) onto the
// matching ItemData/AbilityData assets' Icon field - a menu command instead
// of ~19 manual Inspector drags. Lives under a nested "Editor" folder so
// Unity treats it as Editor-only while Tools/CompileCheck.csproj still
// picks it up (its Assets/Scripts/** glob doesn't care about nesting).
public static class IconWiringTool
{
    private readonly struct Entry
    {
        public readonly string AssetPath;
        public readonly string IconPath;

        public Entry(string assetPath, string iconPath)
        {
            AssetPath = assetPath;
            IconPath = iconPath;
        }
    }

    private const string IconRoot = "Assets/External/6000FantasyIcons/";

    private static readonly Entry[] Entries =
    {
        new Entry("Assets/Resources/Data/Items/GearAmuletOfTheMagi.asset", IconRoot + "ArmorIcons/RingAndNeck_Icons/necklace_24_crystalBlue.png"),
        new Entry("Assets/Resources/Data/Items/GearRingOfTheMagi.asset", IconRoot + "ArmorIcons/RingAndNeck_Icons/Ring_b_05.png"),
        new Entry("Assets/Resources/Data/Items/GearTransmittingBeacon.asset", IconRoot + "ArmorIcons/RingAndNeck_Icons/Ring_27_goldGreen.png"),
        new Entry("Assets/Resources/Data/Items/GearChampionsCloak.asset", IconRoot + "ArmorIcons/ArmorSet_Icons/Cloak/cloak_14.png"),
        new Entry("Assets/Resources/Data/Items/GearArmoredBoots.asset", IconRoot + "ArmorIcons/BasicArmor_Icons/Boots_22_paladin.png"),
        new Entry("Assets/Resources/Data/Items/GearIceCleats.asset", IconRoot + "ArmorIcons/BasicArmor_Icons/Boots_30.png"),
        new Entry("Assets/Resources/Data/Items/GearBootsOfLightness.asset", IconRoot + "MedievalIcons/ArmorMedieval/LeatherBoots3.png"),
        new Entry("Assets/Resources/Data/Items/GearSwiftBoots.asset", IconRoot + "ArmorIcons/BasicArmor_Icons/Boots_44.png"),
        new Entry("Assets/Resources/Data/Items/GearTheEverflow.asset", IconRoot + "MedievalIcons/ResourcesMedieval/GoldCup.png"),
        new Entry("Assets/Resources/Data/Items/GearBroadSword.asset", IconRoot + "WeaponIcons/WeaponIconsVol1/Sword_06.png"),
        new Entry("Assets/Resources/Data/Items/GearArmorbreaker.asset", IconRoot + "WeaponIcons/WeaponIconsVol2/Club_v2_08.png"),
        new Entry("Assets/Resources/Data/Items/GearAegisOfTheUnstoppable.asset", IconRoot + "WeaponIcons/WeaponIconsVol1/shield_31.png"),
        new Entry("Assets/Resources/Data/Items/GearStaff.asset", IconRoot + "WeaponIcons/WeaponIconsVol2/Staff_v2_09.png"),
        new Entry("Assets/Resources/Data/Items/GearTwoHandedAxe.asset", IconRoot + "WeaponIcons/WeaponIconsVol2/Axe_v2_21.png"),
        new Entry("Assets/Resources/Data/Items/GearHuntersBow.asset", IconRoot + "WeaponIcons/WeaponIconsVol2/Bow_v2_09.png"),
        new Entry("Assets/Resources/Data/Items/GearTombOfTheMagi.asset", IconRoot + "WeaponIcons/WeaponIconsVol2/Shield_v2_24.png"),
        new Entry("Assets/Resources/Data/Abilities/AbilityFirebolt.asset", IconRoot + "SkillsIcons/Bonus/Skill1_Nobg/Big_fire_arrow_nobg.png"),
        new Entry("Assets/Resources/Data/Abilities/AbilityIcebolt.asset", IconRoot + "SkillsIcons/Bonus/Skill1_Nobg/Big_frost_arrow_nobg.png"),
        new Entry("Assets/Resources/Data/Abilities/AbilitySoulSiphon.asset", IconRoot + "SkillsIcons/Bonus/Skill1_Standart/Y_fellCloak.png"),
        new Entry("Assets/Resources/Data/Abilities/AbilityRadiantEmbrace.asset", IconRoot + "SkillsIcons/SkillIcons3/SkillVol3_nb/Skill_HolyMagic_nb.png"),
        new Entry("Assets/Resources/Data/Abilities/AbilityEverlivingTouch.asset", IconRoot + "SkillsIcons/SkillIcons3/SkillVol3_nb/Skill_HealingTouch_nb.png"),
        new Entry("Assets/Resources/Data/Abilities/AbilityBlessingOfVitality.asset", IconRoot + "SkillsIcons/Skillicons2/Skill_nobg/skill_209_noBG.png"),
        new Entry("Assets/Resources/Data/Abilities/AbilitySeraphsGrace.asset", IconRoot + "SkillsIcons/SkillIcons3/SkillVol3_nb/Skill_HealingChain_nb.png"),
        new Entry("Assets/Resources/Data/Abilities/AbilityAegisOfArcane.asset", IconRoot + "SkillsIcons/SkillIcons3/SkillVol3_nb/Skill_ShieldUp_nb.png"),
        new Entry("Assets/Resources/Data/Abilities/AbilityRecall.asset", IconRoot + "SkillsIcons/SkillIcons3/SkillVol3_nb/Skill_QuickSand_nb.png"),
        new Entry("Assets/Resources/Data/Abilities/AbilityEcholocation.asset", IconRoot + "SkillsIcons/SkillIcons3/SkillVol3_nb/Skill_Mark_nb.png"),
        new Entry("Assets/Resources/Data/Abilities/AbilityAuraOfRegeneration.asset", IconRoot + "SkillsIcons/SkillIcons3/SkillVol3_nb/Skill_MagicSlowdown_nb.png"),
        new Entry("Assets/Resources/Data/Abilities/AbilityAuraOfReplenishment.asset", IconRoot + "SkillsIcons/SkillIcons3/SkillVol3_nb/Skill_ClearWater_nb.png"),
    };

    [MenuItem("Encounter/Wire Icons")]
    private static void WireIcons()
    {
        int wired = 0;
        int failed = 0;

        foreach (Entry entry in Entries)
        {
            Sprite icon = LoadAsSprite(entry.IconPath);
            if (icon == null)
            {
                Debug.LogWarning($"[IconWiringTool] Could not load sprite at {entry.IconPath} - skipped {entry.AssetPath}.");
                failed++;
                continue;
            }

            Object target = AssetDatabase.LoadAssetAtPath<Object>(entry.AssetPath);
            bool assigned = true;
            if (target is ItemData item) item.Icon = icon;
            else if (target is AbilityData ability) ability.Icon = icon;
            else assigned = false;

            if (!assigned)
            {
                Debug.LogWarning($"[IconWiringTool] Could not load {entry.AssetPath} as ItemData/AbilityData - skipped.");
                failed++;
                continue;
            }

            EditorUtility.SetDirty(target);
            wired++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[IconWiringTool] Wired {wired} icon(s), {failed} failed/skipped.");
    }

    // Asset Store PNGs often import with Texture Type "Default", not
    // "Sprite" - LoadAssetAtPath<Sprite> silently returns null in that
    // case. Coerce the importer and reimport once before giving up.
    private static Sprite LoadAsSprite(string path)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite != null) return sprite;

        if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.SaveAndReimport();
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        return sprite;
    }
}
