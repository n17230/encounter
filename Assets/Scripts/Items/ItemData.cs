using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewItem", menuName = "Game/Item")]
public class ItemData : ScriptableObject
{
    // Stable identity used over the network and in saved profiles.
    public string Id;
    public string ItemName;
    public GearSlot Slot;
    public List<StatBonus> Bonuses = new List<StatBonus>();
}
