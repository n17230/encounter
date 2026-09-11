using System;
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
    public static readonly int GearSlotCount = Enum.GetValues(typeof(GearSlot)).Length;
    public static readonly int MovementActionCount = Enum.GetValues(typeof(MovementAction)).Length;

    public string[] SlotAbilityIds = new string[AbilitySlots];
    public KeyCode[] SlotKeys = new KeyCode[AbilitySlots];
    public bool[] SlotKeyShift = new bool[AbilitySlots];
    public string[] GearIds = new string[GearSlotCount];
    public KeyCode[] MovementKeys = (KeyCode[])MovementInput.Defaults.Clone();
    public float UiScale = 1f;
    // Last server address joined as a client; empty = NetworkBootstrap's default.
    public string ServerAddress = "";

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

    // A profile loaded from disk may predate a change in slot counts or
    // have been hand-edited; make every array the size the code expects.
    public void Normalize()
    {
        Array.Resize(ref SlotAbilityIds, AbilitySlots);
        Array.Resize(ref SlotKeys, AbilitySlots);
        Array.Resize(ref SlotKeyShift, AbilitySlots);
        Array.Resize(ref GearIds, GearSlotCount);
        if (MovementKeys == null || MovementKeys.Length != MovementActionCount)
        {
            MovementKeys = (KeyCode[])MovementInput.Defaults.Clone();
        }
        UiScale = Mathf.Clamp(UiScale <= 0f ? 1f : UiScale, UIScale.Min, UIScale.Max);
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
