using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewItem", menuName = "Encounter/Item")]
public class ItemData : ScriptableObject
{
    // Stable identity used over the network and in saved profiles.
    public string Id;
    public string ItemName;
    public GearSlot Slot;
    public List<StatBonus> Bonuses = new List<StatBonus>();
    public List<EffectImmunity> Immunities = new List<EffectImmunity>();
    public List<ItemAura> Auras = new List<ItemAura>();

    // For MainHand items: what the player's auto-attack swings with. Null on
    // anything that isn't a weapon (the unarmed Fists profile is used then).
    public WeaponData Weapon;
}
