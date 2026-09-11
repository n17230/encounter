using System;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CharacterStats))]
public class CharacterEquipment : NetworkBehaviour
{
    private static readonly int SlotCount = PlayerProfile.GearSlotCount;

    // Auras are re-applied on everyone in range this often, with enough
    // duration to bridge the gap; step out of range and it simply lapses.
    private const float AuraPulseInterval = 1f;
    private const float AuraPulseDuration = 2.5f;

    private CharacterStats stats;
    private readonly ItemData[] equippedItems = new ItemData[SlotCount];
    private bool initialGearApplied;
    private float nextAuraPulse;

    // Server-side view of what the main hand swings with (null = unarmed).
    public WeaponData MainHandWeapon => equippedItems[(int)GearSlot.MainHand] != null ? equippedItems[(int)GearSlot.MainHand].Weapon : null;

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
        SetGearServerRpc(string.Join(";", ProfileStore.Current.GearIds));
    }

    [ServerRpc]
    private void SetGearServerRpc(string joinedItemIds)
    {
        string[] ids = (joinedItemIds ?? "").Split(';');
        for (int slot = 0; slot < SlotCount; slot++)
        {
            ItemData item = slot < ids.Length ? GameDatabase.GetItem(ids[slot]) : null;
            if (item != null && (int)item.Slot != slot) item = null; // wrong-slot items are rejected
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

    private void FixedUpdate()
    {
        if (!IsServer || Time.time < nextAuraPulse) return;
        nextAuraPulse = Time.time + AuraPulseInterval;

        foreach (ItemData item in equippedItems)
        {
            if (item == null || item.Auras.Count == 0) continue;
            foreach (ItemAura aura in item.Auras) PulseAura(aura);
        }
    }

    private void PulseAura(ItemAura aura)
    {
        if (aura.Effect == null) return;

        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            CharacterStats ally = client.PlayerObject.GetComponent<CharacterStats>();
            if (ally == null || ally.CurrentHealth.Value <= 0f) continue;
            if (Vector3.Distance(transform.position, ally.transform.position) > aura.Range) continue;

            ally.ApplyEffect(aura.Effect, AuraPulseDuration, CharacterStats.NoAttacker, HitSource.Aura);
        }
    }

    private void Equip(GearSlot slot, ItemData item)
    {
        int index = (int)slot;
        ItemData previous = equippedItems[index];
        if (previous == item) return;

        if (previous != null)
        {
            foreach (StatType type in Enum.GetValues(typeof(StatType)))
            {
                stats.GetStat(type)?.RemoveAllModifiersFromSource(previous);
            }
            stats.RemoveImmunitiesFromSource(previous);
        }

        equippedItems[index] = item;
        if (item == null) return;

        foreach (StatBonus bonus in item.Bonuses)
        {
            stats.GetStat(bonus.Stat)?.AddModifier(new StatModifier(bonus.Value, bonus.ModifierType, item));
        }
        foreach (EffectImmunity immunity in item.Immunities)
        {
            stats.AddImmunity(immunity, item);
        }
    }
}
