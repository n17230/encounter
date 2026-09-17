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

    // Server-wide item uniqueness: at most one connected player may have a
    // given item Id equipped at a time. Static (one server process, not
    // per-instance) and server-only - clients have no visibility into who
    // else holds what.
    private static readonly Dictionary<string, ulong> globalItemOwners = new Dictionary<string, ulong>();

    private static bool TryClaimItem(ItemData item, ulong clientId)
    {
        if (globalItemOwners.TryGetValue(item.Id, out ulong owner) && owner != clientId) return false;
        globalItemOwners[item.Id] = clientId;
        return true;
    }

    private static void ReleaseItem(ItemData item, ulong clientId)
    {
        if (item != null && globalItemOwners.TryGetValue(item.Id, out ulong owner) && owner == clientId)
        {
            globalItemOwners.Remove(item.Id);
        }
    }

    // Which side (above/below) of each equipped item's HP-threshold
    // effect(s) is currently applied - see UpdateHpThresholds. Keyed by
    // (physical slot, index into that item's HpThresholdEffects).
    private readonly Dictionary<(int slot, int index), bool> thresholdAboveState = new Dictionary<(int, int), bool>();

    // Whether each slot's SingleTargetBonuses are currently applied - see
    // UpdateSingleTargetBonuses. Keyed by physical slot only (one condition
    // per item, not per-index like HpThresholdEffects).
    private readonly Dictionary<int, bool> singleTargetActiveState = new Dictionary<int, bool>();

    // True while any worn item broadcasts the wearer's position to allies'
    // minimaps. Server-written so every client can read it off the wearer.
    public readonly NetworkVariable<bool> BroadcastsLocation =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // AbilityData.IsAuraSpell abilities currently in this caster's loadout -
    // not gear, so not part of equippedItems. No cast/keybind needed: each
    // one pulses continuously (the same way an item's own Auras would) for
    // as long as it stays slotted, and any number can be active at once -
    // see SetActiveAuras, called by PlayerAbilities.SetLoadoutServerRpc
    // whenever the loadout changes.
    private readonly List<AbilityData> activeAuraAbilities = new List<AbilityData>();
    public readonly NetworkVariable<bool> CastAuraRevealsMobs =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Whether specifically Aura of Replenishment/Regeneration are slotted -
    // synced to everyone (unlike activeAuraAbilities, server-only) so
    // AuraGroundVisual can show the right VFX under a player on every
    // client, not just their own. Same pattern as CastAuraRevealsMobs,
    // just per-ability instead of unioned across all active auras.
    public readonly NetworkVariable<bool> HasReplenishmentAura =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<bool> HasRegenerationAura =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public void SetActiveAuras(IEnumerable<AbilityData> auraAbilities)
    {
        if (!IsServer) return;
        activeAuraAbilities.Clear();
        bool revealsMobs = false;
        bool hasReplenishment = false;
        bool hasRegeneration = false;
        foreach (AbilityData ability in auraAbilities)
        {
            activeAuraAbilities.Add(ability);
            revealsMobs |= (ability.AuraReveals & MinimapReveal.Mobs) != 0;
            if (ability.Id == "aura_of_replenishment") hasReplenishment = true;
            if (ability.Id == "aura_of_regeneration") hasRegeneration = true;
        }
        CastAuraRevealsMobs.Value = revealsMobs;
        HasReplenishmentAura.Value = hasReplenishment;
        HasRegenerationAura.Value = hasRegeneration;
    }

    // Server-side view of what the main hand swings with (null = unarmed).
    public WeaponData MainHandWeapon => equippedItems[(int)GearSlot.MainHand] != null ? equippedItems[(int)GearSlot.MainHand].Weapon : null;

    // Server-side check for AbilityData.RequiresShield gating.
    public bool HasShieldEquipped => equippedItems[(int)GearSlot.OffHand] != null && equippedItems[(int)GearSlot.OffHand].IsShield;

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
        if (IsServer)
        {
            foreach (ItemData item in equippedItems) ReleaseItem(item, OwnerClientId);
        }
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

            // A two-handed main-hand weapon occupies the off hand too - since
            // MainHand (slot 11) is always processed before OffHand (slot 12)
            // in this loop, equippedItems[MainHand] already reflects this
            // sync's result by the time OffHand is reached.
            if (physicalSlot == GearSlot.OffHand
                && equippedItems[(int)GearSlot.MainHand] != null
                && equippedItems[(int)GearSlot.MainHand].TwoHanded)
            {
                item = null;
            }

            // Server-wide: someone else already wearing this item Id blocks
            // equipping it here. Claiming your own already-held item is a
            // harmless no-op.
            if (item != null && !TryClaimItem(item, OwnerClientId)) item = null;

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

        foreach (AbilityData auraAbility in activeAuraAbilities)
        {
            if (auraAbility.Effect == null) continue;
            PulseAura(new ItemAura { Effect = auraAbility.Effect, Range = auraAbility.AuraRange });
        }

        UpdateHpThresholds();
        UpdateSingleTargetBonuses();
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

    // Checked on the same once-a-second cadence as HP thresholds/auras -
    // e.g. Hunter's Cloak. Active while the wearer has damaged/debuffed at
    // most one distinct enemy within the item's own SingleTargetWindowSeconds
    // (CharacterStats.DistinctEnemiesTouchedWithin/RecordEnemyInteraction).
    private void UpdateSingleTargetBonuses()
    {
        for (int slot = 0; slot < SlotCount; slot++)
        {
            ItemData item = equippedItems[slot];
            if (item == null || item.SingleTargetBonuses.Count == 0) continue;

            bool active = stats.DistinctEnemiesTouchedWithin(item.SingleTargetWindowSeconds) <= 1;
            if (singleTargetActiveState.TryGetValue(slot, out bool previouslyActive) && previouslyActive == active) continue;
            singleTargetActiveState[slot] = active;

            // Distinct from plain `item` (Bonuses' source) and (item, i, bool)
            // (HpThresholdEffects' source) - can't collide with either.
            object source = (item, "singleTarget");
            foreach (StatType type in Enum.GetValues(typeof(StatType)))
            {
                stats.GetStat(type)?.RemoveAllModifiersFromSource(source);
            }

            if (!active) continue;
            foreach (StatBonus bonus in item.SingleTargetBonuses)
            {
                stats.GetStat(bonus.Stat)?.AddModifier(new StatModifier(bonus.Value, bonus.ModifierType, source));
            }
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
            ReleaseItem(previous, OwnerClientId);

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

            if (previous.SingleTargetBonuses.Count > 0)
            {
                foreach (StatType type in Enum.GetValues(typeof(StatType)))
                {
                    stats.GetStat(type)?.RemoveAllModifiersFromSource((previous, "singleTarget"));
                }
                singleTargetActiveState.Remove(index);
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
