using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct StatBonus
{
    public StatType Stat;
    public StatModifierType ModifierType;
    public float Value;
}

[CreateAssetMenu(fileName = "NewItem", menuName = "Game/Item")]
public class ItemData : ScriptableObject
{
    public string ItemName;
    public GearSlot Slot;
    public List<StatBonus> Bonuses = new List<StatBonus>();
}
