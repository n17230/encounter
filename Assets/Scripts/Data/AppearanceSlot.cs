// Which independent cosmetic slot an AppearancePieceData fills. Headwear
// isn't here - AppearanceHeadwearData is its own asset type (a standalone
// attachable prefab, not a toggled node inside the body rig). Accessories
// (capes, pauldrons, etc.) aren't here either - AppearanceAccessoryData is a
// free multi-select pool, not a single-choice slot like these.
public enum AppearanceSlot
{
    Top,
    Bottom,
    Eyebrows,
    Eyes,
    Mouth,
    Hair,
    FacialHair
}
