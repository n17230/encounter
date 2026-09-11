using System;

// What an item lets the wearer see on the minimap. The map is blank by
// default; the union of every equipped item's reveals decides what draws.
[Flags]
public enum MinimapReveal
{
    None = 0,
    Players = 1 << 0,
    Mobs = 1 << 1,
}
