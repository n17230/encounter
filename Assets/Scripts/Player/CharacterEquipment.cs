using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CharacterStats))]
public class CharacterEquipment : NetworkBehaviour
{
    private static readonly int SlotCount = PlayerProfile.EquipmentSlotCount;

    // Auras are re-applied on everyone in range this often, with enough
    // duration to bridge the gap; step out of range and it simply lapses.
    private const float AuraPulseInterval = 1f;
    private const float AuraPulseDuration = 2.5f;

    private CharacterStats stats;
    private readonly ItemData[] equippedItems = new ItemData[SlotCount];
    private bool initialEquipmentApplied;
    private float nextAuraPulse;

    // Server-wide item uniqueness: at most one connected player may have a
    // given item Id equipped at a time. Static (one server process, not
    // per-instance) and server-only - clients have no visibility into who
    // else holds what. The one map for items: the lobby's gear confirm
    // (LobbyState) and this component's spawn-time/menu-close sync both
    // claim through ValidateAndClaim below, keyed by the same clientId, so
    // the sync at Start simply re-claims what the lobby already granted.
    private static readonly OwnershipMap globalItemOwners = new OwnershipMap();
    private static readonly List<string> keptIdScratch = new List<string>();

    // Called when a server session ends / the process starts - see NetworkBootstrap.
    public static void ResetServerSessionState()
    {
        globalItemOwners.Clear();
    }

    // Frees every item the client holds - its player despawning, or (before
    // any player exists) the lobby seeing it disconnect.
    public static void ReleaseAllClaims(ulong clientId)
    {
        globalItemOwners.ReleaseAll(clientId);
    }

    // The shared validation core: turns a ';'-joined per-slot Id string into
    // what the server will actually let this client wear, claiming each kept
    // item for the client and releasing anything the client held that isn't
    // in the result. Static so LobbyState can run it for a client that has
    // no CharacterEquipment yet; SetEquipmentServerRpc applies the result to
    // the instance. A null entry is an empty slot or a dropped item - wrong
    // slot, off hand under a two-hander, or someone else already wearing it.
    public static ItemData[] ValidateAndClaim(string joinedItemIds, ulong clientId)
    {
        string[] ids = (joinedItemIds ?? "").Split(';');
        ItemData[] result = new ItemData[SlotCount];
        for (int slot = 0; slot < SlotCount; slot++)
        {
            ItemData item = slot < ids.Length ? GameDatabase.GetItem(ids[slot]) : null;
            EquipmentSlot physicalSlot = (EquipmentSlot)slot;

            // A ring item's own Slot is just "Ring1" as a category; it's
            // valid in either physical ring slot. Everything else needs an
            // exact match.
            bool validPlacement = item != null && (item.Slot.IsRing() ? physicalSlot.IsRing() : item.Slot == physicalSlot);
            if (!validPlacement) item = null;

            // A two-handed main-hand weapon occupies the off hand too - since
            // MainHand (slot 11) is always processed before OffHand (slot 12)
            // in this loop, result[MainHand] already reflects this sync by
            // the time OffHand is reached.
            if (physicalSlot == EquipmentSlot.OffHand
                && result[(int)EquipmentSlot.MainHand] != null
                && result[(int)EquipmentSlot.MainHand].TwoHanded)
            {
                item = null;
            }

            // Server-wide: someone else already wearing this item Id blocks
            // equipping it here. Claiming your own already-held item is a
            // harmless no-op.
            if (item != null && !globalItemOwners.TryClaim(item.Id, clientId)) item = null;

            result[slot] = item;
        }

        keptIdScratch.Clear();
        foreach (ItemData kept in result)
        {
            if (kept != null) keptIdScratch.Add(kept.Id);
        }
        globalItemOwners.ReleaseAllExcept(clientId, keptIdScratch);
        return result;
    }

    // The ';'-joined form of a validated result - what LobbyState stores in
    // the entry and sends back to the client (empty segment = empty slot).
    public static string JoinIds(ItemData[] items)
    {
        string[] ids = new string[items.Length];
        for (int i = 0; i < items.Length; i++) ids[i] = items[i] != null ? items[i].Id : "";
        return string.Join(";", ids);
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

    // Which item Id (if any) is in MainHand/OffHand, purely so
    // CharacterWeaponVisual can show the right 3D model on every client -
    // Server-written (not owner-written like CharacterAppearance's cosmetic
    // Ids) since it must reflect what the server actually validated as
    // equipped (two-handed clearing, server-wide uniqueness), not whatever
    // the owner's client locally thinks is equipped.
    public readonly NetworkVariable<FixedString32Bytes> MainHandItemId =
        new NetworkVariable<FixedString32Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<FixedString32Bytes> OffHandItemId =
        new NetworkVariable<FixedString32Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // AbilityData.IsAuraSpell abilities currently in this caster's loadout -
    // not equipment, so not part of equippedItems. No cast/keybind needed: each
    // one pulses continuously (the same way an item's own Auras would) for
    // as long as it stays slotted, and any number can be active at once -
    // see SetActiveAuras, called by PlayerAbilities.SetLoadoutServerRpc
    // whenever the loadout changes.
    private readonly List<AbilityData> activeAuraAbilities = new List<AbilityData>();
    public readonly NetworkVariable<bool> CastAuraRevealsMobs =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Whether specifically Aura: Replenish/Regeneration are slotted -
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
    public WeaponData MainHandWeapon => equippedItems[(int)EquipmentSlot.MainHand] != null ? equippedItems[(int)EquipmentSlot.MainHand].Weapon : null;

    // Server-side check for AbilityData.RequiresShield gating.
    public bool HasShieldEquipped => equippedItems[(int)EquipmentSlot.OffHand] != null && equippedItems[(int)EquipmentSlot.OffHand].IsShield;

    private void Awake()
    {
        stats = GetComponent<CharacterStats>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        SyncEquipmentToServer();
        MainMenu.Closed += SyncEquipmentToServer;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) ReleaseAllClaims(OwnerClientId);
        if (!IsOwner) return;
        MainMenu.Closed -= SyncEquipmentToServer;
    }

    private void SyncEquipmentToServer()
    {
        SetEquipmentServerRpc(string.Join(";", ProfileStore.Current.EquipmentIds));
    }

    [ServerRpc]
    private void SetEquipmentServerRpc(string joinedItemIds)
    {
        // Validation, placement rules and the uniqueness claims all live in
        // the shared core (also what the lobby confirm runs); only applying
        // the result to this character's stats is instance work.
        ItemData[] validated = ValidateAndClaim(joinedItemIds, OwnerClientId);
        for (int slot = 0; slot < SlotCount; slot++)
        {
            Equip((EquipmentSlot)slot, validated[slot]);
        }

        bool broadcasts = false;
        foreach (ItemData equipped in equippedItems) broadcasts |= equipped != null && equipped.BroadcastsLocation;
        BroadcastsLocation.Value = broadcasts;

        MainHandItemId.Value = equippedItems[(int)EquipmentSlot.MainHand]?.Id ?? "";
        OffHandItemId.Value = equippedItems[(int)EquipmentSlot.OffHand]?.Id ?? "";

        // Only the spawn-time application tops the player off; a mid-fight
        // equipment swap from the menu must not double as a free full heal.
        if (!initialEquipmentApplied)
        {
            initialEquipmentApplied = true;
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

        // A corpse projects nothing (item and spell auras alike) - allies'
        // aura effects simply lapse within AuraPulseDuration of the death.
        // The threshold/single-target updates below keep running regardless.
        if (stats.IsAlive)
        {
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

    private void Equip(EquipmentSlot slot, ItemData item)
    {
        int index = (int)slot;
        ItemData previous = equippedItems[index];
        if (previous == item) return;

        // No ownership release here: ValidateAndClaim already released every
        // Id that isn't in the new set before this is reached (a ring moving
        // from Ring1 to Ring2 stays claimed throughout).
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
