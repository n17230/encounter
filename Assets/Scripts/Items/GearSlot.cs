// Matches the slot list from DESIGN_IDEAS.md: helmet, necklace, chest,
// cape, gloves, belt, legs, boots, 2 rings, a trinket, main hand, off hand.
public enum GearSlot
{
    Helmet,
    Necklace,
    Chest,
    Cape,
    Gloves,
    Belt,
    Legs,
    Boots,
    Ring1,
    Ring2,
    Trinket,
    MainHand,
    OffHand
}

public static class GearSlotExtensions
{
    // Ring1 and Ring2 are interchangeable: a ring item declares its Slot as
    // Ring1 (its category, not a specific physical slot) and can be equipped
    // into either physical ring slot.
    public static bool IsRing(this GearSlot slot) => slot == GearSlot.Ring1 || slot == GearSlot.Ring2;
}
