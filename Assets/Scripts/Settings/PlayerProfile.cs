using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Everything the local player has chosen that should survive a restart:
// skill loadout + hotkeys, equipped gear, movement keys, UI scale. Stored
// as data-asset Ids (see GameDatabase) and plain KeyCodes so it's a flat
// JSON file. Not yet the account-backed, unlock-gated profile from
// DESIGN_IDEAS.md - but it's the shape that one will grow from.
[Serializable]
public class PlayerProfile
{
    public const int AbilitySlots = 8;
    // GearSlot's underlying values aren't contiguous (Legs is pinned to 6
    // so removing Belt didn't shift every later slot's serialized value),
    // so GearIds must be sized by the highest underlying value + 1, not by
    // how many names the enum has.
    public static readonly int GearSlotCount = HighestGearSlotValue() + 1;
    public static readonly int MovementActionCount = Enum.GetValues(typeof(MovementAction)).Length;

    private static int HighestGearSlotValue()
    {
        int highest = 0;
        foreach (GearSlot slot in Enum.GetValues(typeof(GearSlot)))
        {
            if ((int)slot > highest) highest = (int)slot;
        }
        return highest;
    }

    public string[] SlotAbilityIds = new string[AbilitySlots];
    public KeyCode[] SlotKeys = new KeyCode[AbilitySlots];
    public bool[] SlotKeyShift = new bool[AbilitySlots];
    public string[] GearIds = new string[GearSlotCount];
    public KeyCode[] MovementKeys = (KeyCode[])MovementInput.Defaults.Clone();
    public float UiScale = 1f;
    // Multiplier applied on top of every hardcoded look-sensitivity value
    // (PlayerCamera's pitch/free-look, PlayerMovement's turn) - 1 = the
    // project's original feel, unchanged.
    public float LookSensitivity = 1f;
    // Camera zoom distance (Up/Down arrow) - 0 means "never set, use
    // whatever distance PlayerCamera's prefab authored" (see
    // PlayerCamera.Awake), unlike UiScale/LookSensitivity which always
    // hold a real value.
    public float CameraZoomDistance = 0f;
    // Last server address joined as a client; empty = NetworkBootstrap's default.
    public string ServerAddress = "";

    // Cosmetic appearance - see CharacterAppearance. Entirely separate from
    // GearIds (no stat effect); AppearanceHeadwearId/AppearanceFacialHairId
    // empty means none. AppearanceAccessoryIds is a ';'-joined list (free
    // multi-select, any combination) - see CharacterAppearanceApplier.
    public bool AppearanceIsFemale = false;
    public string AppearanceTopId = "";
    public string AppearanceBottomId = "";
    public string AppearanceHeadwearId = "";
    public string AppearanceEyebrowsId = "";
    public string AppearanceEyesId = "";
    public string AppearanceMouthId = "";
    public string AppearanceHairId = "";
    public string AppearanceFacialHairId = "";
    public string AppearanceAccessoryIds = "";
    public int AppearanceBodyColorIndex = 0;
    public int AppearanceObjectColorIndex = 0;

    public AbilityData GetSlotAbility(int slot) => GameDatabase.GetAbility(SlotAbilityIds[slot]);

    public void SetSlotAbility(int slot, AbilityData ability)
    {
        SlotAbilityIds[slot] = ability != null ? ability.Id : null;
    }

    public KeyBindingOption? GetSlotKey(int slot)
    {
        if (SlotKeys[slot] == KeyCode.None) return null;
        return new KeyBindingOption(SlotKeys[slot], SlotKeyShift[slot]);
    }

    public void SetSlotKey(int slot, KeyBindingOption? key)
    {
        SlotKeys[slot] = key.HasValue ? key.Value.Key : KeyCode.None;
        SlotKeyShift[slot] = key.HasValue && key.Value.RequiresShift;
    }

    public int IndexOfAbility(AbilityData ability)
    {
        if (ability == null) return -1;
        return Array.IndexOf(SlotAbilityIds, ability.Id);
    }

    public ItemData GetGear(GearSlot slot) => GameDatabase.GetItem(GearIds[(int)slot]);

    public void SetGear(GearSlot slot, ItemData item)
    {
        GearIds[(int)slot] = item != null ? item.Id : null;
    }

    public bool IsEquipped(ItemData item) => item != null && Array.IndexOf(GearIds, item.Id) >= 0;

    public AppearanceGender Gender => AppearanceIsFemale ? AppearanceGender.Female : AppearanceGender.Male;

    public AppearancePieceData GetAppearanceTop() => GameDatabase.GetAppearancePiece(AppearanceTopId);
    public AppearancePieceData GetAppearanceBottom() => GameDatabase.GetAppearancePiece(AppearanceBottomId);
    public AppearanceHeadwearData GetAppearanceHeadwear() => GameDatabase.GetAppearanceHeadwear(AppearanceHeadwearId);
    public AppearancePieceData GetAppearanceEyebrows() => GameDatabase.GetAppearancePiece(AppearanceEyebrowsId);
    public AppearancePieceData GetAppearanceEyes() => GameDatabase.GetAppearancePiece(AppearanceEyesId);
    public AppearancePieceData GetAppearanceMouth() => GameDatabase.GetAppearancePiece(AppearanceMouthId);
    public AppearancePieceData GetAppearanceHair() => GameDatabase.GetAppearancePiece(AppearanceHairId);
    public AppearancePieceData GetAppearanceFacialHair() => GameDatabase.GetAppearancePiece(AppearanceFacialHairId);

    public bool HasAccessory(string id) => CharacterAppearanceApplier.ParseAccessoryIds(AppearanceAccessoryIds).Contains(id);

    public void ToggleAccessory(string id, bool enabled)
    {
        HashSet<string> ids = CharacterAppearanceApplier.ParseAccessoryIds(AppearanceAccessoryIds);
        if (enabled) ids.Add(id); else ids.Remove(id);
        AppearanceAccessoryIds = string.Join(";", ids);
    }

    // A profile loaded from disk may predate a change in slot counts or
    // have been hand-edited; make every array the size the code expects.
    public void Normalize()
    {
        Array.Resize(ref SlotAbilityIds, AbilitySlots);
        Array.Resize(ref SlotKeys, AbilitySlots);
        Array.Resize(ref SlotKeyShift, AbilitySlots);
        Array.Resize(ref GearIds, GearSlotCount);

        // A slot holding an Id that no longer resolves (the ability was
        // renamed or removed since this profile was saved) would otherwise
        // render as "(empty)" in the UI while still blocking that slot from
        // being treated as available - clear it so the two agree.
        for (int i = 0; i < SlotAbilityIds.Length; i++)
        {
            if (!string.IsNullOrEmpty(SlotAbilityIds[i]) && GameDatabase.GetAbility(SlotAbilityIds[i]) == null)
            {
                SlotAbilityIds[i] = null;
            }
        }
        // A profile saved before an action was added keeps its existing
        // bindings and takes the default for the new ones.
        if (MovementKeys == null) MovementKeys = new KeyCode[0];
        if (MovementKeys.Length != MovementActionCount)
        {
            KeyCode[] merged = (KeyCode[])MovementInput.Defaults.Clone();
            Array.Copy(MovementKeys, merged, Math.Min(MovementKeys.Length, merged.Length));
            MovementKeys = merged;
        }
        UiScale = Mathf.Clamp(UiScale <= 0f ? 1f : UiScale, UIScale.Min, UIScale.Max);
        LookSensitivity = Mathf.Clamp(LookSensitivity <= 0f ? 1f : LookSensitivity, LookSensitivityScale.Min, LookSensitivityScale.Max);
        if (CameraZoomDistance > 0f) CameraZoomDistance = Mathf.Clamp(CameraZoomDistance, CameraZoomScale.Min, CameraZoomScale.Max);

        // A slot Id that no longer resolves, or no longer matches the
        // current gender (e.g. the gender was just switched), is cleared the
        // same way a stale ability Id is above - then re-defaulted to the
        // first matching option so a fresh or just-switched profile doesn't
        // render with a blank face or bare skin. Eyebrows/Eyes/Mouth
        // need exactly one active the same as Top/Bottom always have.
        NormalizeRequiredSlot(AppearanceSlot.Top, ref AppearanceTopId);
        NormalizeRequiredSlot(AppearanceSlot.Bottom, ref AppearanceBottomId);
        NormalizeRequiredSlot(AppearanceSlot.Eyebrows, ref AppearanceEyebrowsId);
        NormalizeRequiredSlot(AppearanceSlot.Eyes, ref AppearanceEyesId);
        NormalizeRequiredSlot(AppearanceSlot.Mouth, ref AppearanceMouthId);

        // Unlike the slots above, "none" is a valid, common choice for
        // hair (e.g. a full helmet with no hair sticking out), headwear,
        // and facial hair - a stale/mismatched Id is just cleared, never
        // re-defaulted to some other hairstyle/headwear/beard.
        NormalizeOptionalSlot(AppearanceSlot.Hair, ref AppearanceHairId);
        NormalizeOptionalSlot(AppearanceSlot.FacialHair, ref AppearanceFacialHairId);
        if (!string.IsNullOrEmpty(AppearanceHeadwearId))
        {
            AppearanceHeadwearData headwear = GetAppearanceHeadwear();
            if (headwear == null || (headwear.Gender != AppearanceGender.Unisex && headwear.Gender != Gender))
            {
                AppearanceHeadwearId = "";
            }
        }

        // Free multi-select accessories: drop any Id that no longer
        // resolves or no longer matches the current gender.
        List<string> validAccessoryIds = new List<string>();
        foreach (string id in CharacterAppearanceApplier.ParseAccessoryIds(AppearanceAccessoryIds))
        {
            AppearanceAccessoryData accessory = GameDatabase.GetAppearanceAccessory(id);
            if (accessory != null && accessory.Gender == Gender) validAccessoryIds.Add(id);
        }
        AppearanceAccessoryIds = string.Join(";", validAccessoryIds);

        AppearanceColorPalette palette = GameDatabase.Palette;
        int bodyColorCount = palette != null && palette.BodyColors != null ? palette.BodyColors.Length : 1;
        int objectColorCount = palette != null && palette.ObjectColors != null ? palette.ObjectColors.Length : 1;
        AppearanceBodyColorIndex = Mathf.Clamp(AppearanceBodyColorIndex, 0, Mathf.Max(0, bodyColorCount - 1));
        AppearanceObjectColorIndex = Mathf.Clamp(AppearanceObjectColorIndex, 0, Mathf.Max(0, objectColorCount - 1));
    }

    private void NormalizeRequiredSlot(AppearanceSlot slot, ref string id)
    {
        AppearancePieceData piece = GameDatabase.GetAppearancePiece(id);
        if (piece == null || piece.Slot != slot || piece.Gender != Gender) id = "";

        if (string.IsNullOrEmpty(id))
        {
            foreach (AppearancePieceData candidate in GameDatabase.AppearancePieces)
            {
                if (candidate.Slot == slot && candidate.Gender == Gender) { id = candidate.Id; break; }
            }
        }
    }

    private void NormalizeOptionalSlot(AppearanceSlot slot, ref string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        AppearancePieceData piece = GameDatabase.GetAppearancePiece(id);
        if (piece == null || piece.Slot != slot || piece.Gender != Gender) id = "";
    }
}

public static class ProfileStore
{
    private static PlayerProfile current;

    public static PlayerProfile Current
    {
        get
        {
            if (current == null) Load();
            return current;
        }
    }

    private static string FilePath => Path.Combine(Application.persistentDataPath, "profile.json");

    public static void Load()
    {
        current = new PlayerProfile();
        try
        {
            if (File.Exists(FilePath))
            {
                JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), current);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ProfileStore] Could not read {FilePath}: {e.Message}. Using defaults.");
            current = new PlayerProfile();
        }
        current.Normalize();
    }

    public static void Save()
    {
        if (current == null) return;
        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(current, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ProfileStore] Could not write {FilePath}: {e.Message}");
        }
    }
}
