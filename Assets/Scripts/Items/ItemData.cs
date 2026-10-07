using System;
using System.Collections.Generic;
using UnityEngine;

// A pair of alternate stat-bonus sets a wearer flips between depending on
// their current health percentage - e.g. Barbarian's Mantle. Handled by
// CharacterEquipment, not the plain always-on Bonuses list, since which
// set is active changes dynamically as health changes.
[Serializable]
public class HpThresholdEffect
{
    [Range(0f, 1f)] public float HealthPercentThreshold = 0.5f;
    public List<StatBonus> AboveThresholdBonuses = new List<StatBonus>();
    public List<StatBonus> BelowThresholdBonuses = new List<StatBonus>();
}

[CreateAssetMenu(fileName = "NewItem", menuName = "Encounter/Item")]
public class ItemData : ScriptableObject
{
    // Stable identity used over the network and in saved profiles.
    public string Id;
    public string ItemName;
    public EquipmentSlot Slot;
    public List<StatBonus> Bonuses = new List<StatBonus>();
    // Empty for every item except ones with health-percentage-conditional
    // bonuses (currently just Barbarian's Mantle) - see HpThresholdEffect.
    public List<HpThresholdEffect> HpThresholdEffects = new List<HpThresholdEffect>();
    public List<EffectImmunity> Immunities = new List<EffectImmunity>();
    public List<ItemAura> Auras = new List<ItemAura>();
    public MinimapReveal Reveals = MinimapReveal.None;

    // The wearer shows up on every ally's minimap, whatever they can see.
    public bool BroadcastsLocation;

    // For MainHand items: what the player's auto-attack swings with. Null on
    // anything that isn't a weapon (the unarmed Fists profile is used then).
    public WeaponData Weapon;

    // Only meaningful on a MainHand item: occupies OffHand too, so the two
    // can never both be equipped at once - see
    // CharacterEquipment.SetEquipmentServerRpc (authoritative) and MainMenu's
    // equip click handler (mirrors it for immediate UX).
    public bool TwoHanded = false;

    // Wearer can press Jump again while airborne to hover in place
    // (gravity suspended, WASD still steers) for a few seconds - see
    // PlayerMovement.hoverDuration. E.g. Boots of Lightness.
    public bool GrantsAirHover = false;

    // An OffHand item that counts as a shield for AbilityData
    // .RequiresShield gating (e.g. Aegis of the Ancient) - see
    // CharacterEquipment.HasShieldEquipped.
    public bool IsShield = false;

    // Read by the Equipment panel's equip/inventory grids - wired via the
    // Encounter/Wire Icons Editor tool, not by hand. Null for whichever
    // items weren't given an icon yet (e.g. Ember Stone, Holy Scepter) -
    // those just render an empty icon box.
    public Sprite Icon;

    // Only meaningful on MainHand/OffHand items - the 3D model
    // CharacterWeaponVisual attaches to the wearer's equip socket while
    // this item is equipped. Null = nothing shown. AttachProfile is how the
    // model sits in the socket - shared per weapon category (every sword
    // points at the same Sword profile), so a new item of an existing
    // category needs no tuning at all. Null profile = no offset.
    public GameObject WeaponModelPrefab;
    public WeaponAttachProfile AttachProfile;

    // Active only while CharacterStats.DistinctEnemiesTouchedWithin
    // (SingleTargetWindowSeconds) is <= 1 - i.e. this player has only
    // damaged/debuffed a single enemy within that rolling window. Handled
    // by CharacterEquipment.UpdateSingleTargetBonuses, evaluated on the
    // same once-a-second cadence as HpThresholdEffects, not the plain
    // always-on Bonuses list. Empty for every item except Hunter's Cloak.
    public List<StatBonus> SingleTargetBonuses = new List<StatBonus>();
    public float SingleTargetWindowSeconds = 12f;
}
