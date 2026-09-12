// Matches the slot list from ARCHITECTURE_NOTES.md: helmet, necklace,
// chest, cape, gloves, legs, boots, 2 rings, a trinket, main hand, off
// hand. Legs is explicitly = 6 (not auto-incrementing from Gloves) so
// removing Belt (formerly 5) doesn't shift every later slot's underlying
// value - those are serialized directly into existing item assets.
public enum GearSlot
{
    Helmet,
    Necklace,
    Chest,
    Cape,
    Gloves,
    Legs = 6,
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
