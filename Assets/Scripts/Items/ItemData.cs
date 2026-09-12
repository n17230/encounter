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
    public GearSlot Slot;
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
    // CharacterEquipment.SetGearServerRpc (authoritative) and MainMenu's
    // gear-equip click handler (mirrors it for immediate UX).
    public bool TwoHanded = false;
}
