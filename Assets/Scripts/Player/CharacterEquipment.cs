using System;
using System.Collections.Generic;
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

    // Which side (above/below) of each equipped item's HP-threshold
    // effect(s) is currently applied - see UpdateHpThresholds. Keyed by
    // (physical slot, index into that item's HpThresholdEffects).
    private readonly Dictionary<(int slot, int index), bool> thresholdAboveState = new Dictionary<(int, int), bool>();

    // True while any worn item broadcasts the wearer's position to allies'
    // minimaps. Server-written so every client can read it off the wearer.
    public readonly NetworkVariable<bool> BroadcastsLocation =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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
            GearSlot physicalSlot = (GearSlot)slot;

            // A ring item's own Slot is just "Ring1" as a category; it's
            // valid in either physical ring slot. Everything else needs an
            // exact match.
            bool validPlacement = item != null && (item.Slot.IsRing() ? physicalSlot.IsRing() : item.Slot == physicalSlot);
            if (!validPlacement) item = null;

            Equip(physicalSlot, item);
        }

        bool broadcasts = false;
        foreach (ItemData equipped in equippedItems) broadcasts |= equipped != null && equipped.BroadcastsLocation;
        BroadcastsLocation.Value = broadcasts;

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

        UpdateHpThresholds();
    }

    // Checked on the same once-a-second cadence as auras - plenty
    // responsive for a defensive/offensive stance swap, and avoids a
    // second per-frame loop. E.g. Barbarian's Mantle.
    private void UpdateHpThresholds()
    {
        if (stats.MaxHealth.Value <= 0f) return;
        float healthFraction = stats.CurrentHealth.Value / stats.MaxHealth.Value;

        for (int slot = 0; slot < SlotCount; slot++)
        {
            ItemData item = equippedItems[slot];
            if (item == null || item.HpThresholdEffects.Count == 0) continue;

            for (int i = 0; i < item.HpThresholdEffects.Count; i++)
            {
                HpThresholdEffect threshold = item.HpThresholdEffects[i];
                bool isAbove = healthFraction > threshold.HealthPercentThreshold;

                var key = (slot, i);
                if (thresholdAboveState.TryGetValue(key, out bool previouslyAbove) && previouslyAbove == isAbove) continue;
                thresholdAboveState[key] = isAbove;

                ApplyThresholdBonuses(item, i, threshold, isAbove);
            }
        }
    }

    private void ApplyThresholdBonuses(ItemData item, int index, HpThresholdEffect threshold, bool isAbove)
    {
        object aboveSource = (item, index, true);
        object belowSource = (item, index, false);

        foreach (StatType type in Enum.GetValues(typeof(StatType)))
        {
            stats.GetStat(type)?.RemoveAllModifiersFromSource(aboveSource);
            stats.GetStat(type)?.RemoveAllModifiersFromSource(belowSource);
        }

        List<StatBonus> activeBonuses = isAbove ? threshold.AboveThresholdBonuses : threshold.BelowThresholdBonuses;
        object activeSource = isAbove ? aboveSource : belowSource;
        foreach (StatBonus bonus in activeBonuses)
        {
            stats.GetStat(bonus.Stat)?.AddModifier(new StatModifier(bonus.Value, bonus.ModifierType, activeSource));
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

            for (int i = 0; i < previous.HpThresholdEffects.Count; i++)
            {
                foreach (StatType type in Enum.GetValues(typeof(StatType)))
                {
                    stats.GetStat(type)?.RemoveAllModifiersFromSource((previous, i, true));
                    stats.GetStat(type)?.RemoveAllModifiersFromSource((previous, i, false));
                }
                thresholdAboveState.Remove((index, i));
            }
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
