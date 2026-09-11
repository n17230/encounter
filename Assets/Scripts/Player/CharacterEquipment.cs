using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CharacterStats))]
public class CharacterEquipment : NetworkBehaviour
{
    private static readonly int SlotCount = System.Enum.GetValues(typeof(GearSlot)).Length;

    // All gear items that exist in the game, in a fixed order shared with
    // MainMenu's "Available Gear" list - same pool-index-over-RPC pattern
    // as PlayerAbilities.abilityPool, since ItemData ScriptableObjects
    // can't be sent directly over an RPC.
    [SerializeField] private List<ItemData> itemPool = new List<ItemData>();

    private CharacterStats stats;
    private readonly ItemData[] equippedItems = new ItemData[SlotCount];
    private bool initialGearApplied;

    private void Awake()
    {
        stats = GetComponent<CharacterStats>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        SyncGearToServer();
        MainMenu.Closed += SyncGearToServer;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;
        MainMenu.Closed -= SyncGearToServer;
    }

    private void SyncGearToServer()
    {
        int[] slotToPoolIndex = new int[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            ItemData equipped = GearSelection.EquippedItems[i];
            slotToPoolIndex[i] = equipped != null ? itemPool.IndexOf(equipped) : -1;
        }
        SetGearServerRpc(slotToPoolIndex);
    }

    [ServerRpc]
    private void SetGearServerRpc(int[] slotToPoolIndex)
    {
        for (int slot = 0; slot < SlotCount && slot < slotToPoolIndex.Length; slot++)
        {
            int poolIndex = slotToPoolIndex[slot];
            ItemData item = (poolIndex >= 0 && poolIndex < itemPool.Count) ? itemPool[poolIndex] : null;
            Equip((GearSlot)slot, item);
        }

        // Only the spawn-time application tops the player off; a mid-fight
        // gear swap from the menu must not double as a free full heal.
        if (!initialGearApplied)
        {
            initialGearApplied = true;
            stats.RestoreFull();
        }
        else
        {
            stats.ClampToMax();
        }
    }

    private void Equip(GearSlot slot, ItemData item)
    {
        int index = (int)slot;
        ItemData previous = equippedItems[index];
        if (previous != null) RemoveBonuses(previous);

        equippedItems[index] = item;
        if (item != null) ApplyBonuses(item);
    }

    private void ApplyBonuses(ItemData item)
    {
        foreach (StatBonus bonus in item.Bonuses)
        {
            GetStat(bonus.Stat)?.AddModifier(new StatModifier(bonus.Value, bonus.ModifierType, item));
        }
    }

    private void RemoveBonuses(ItemData item)
    {
        GetStat(StatType.MaxHealth)?.RemoveAllModifiersFromSource(item);
        GetStat(StatType.HealthRegenRate)?.RemoveAllModifiersFromSource(item);
        GetStat(StatType.MaxMana)?.RemoveAllModifiersFromSource(item);
        GetStat(StatType.ManaRegenRate)?.RemoveAllModifiersFromSource(item);
        GetStat(StatType.RunSpeed)?.RemoveAllModifiersFromSource(item);
        GetStat(StatType.Armor)?.RemoveAllModifiersFromSource(item);
    }

    private Stat GetStat(StatType type)
    {
        switch (type)
        {
            case StatType.MaxHealth: return stats.MaxHealth;
            case StatType.HealthRegenRate: return stats.HealthRegenRate;
            case StatType.MaxMana: return stats.MaxMana;
            case StatType.ManaRegenRate: return stats.ManaRegenRate;
            case StatType.RunSpeed: return stats.RunSpeed;
            case StatType.Armor: return stats.Armor;
            default: return null;
        }
    }
}
