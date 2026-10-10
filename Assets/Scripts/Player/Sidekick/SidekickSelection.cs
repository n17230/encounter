using System;

// A player's chosen Sidekick presets, by preset Name (the stable key -
// Sidekick's integer IDs are autoincrement values local to a DB file).
// "" = not chosen: a part/body-shape preset falls back to the catalog's
// first entry when built (see SidekickPresetCatalog.Resolve), a color
// preset leaves the base material untouched. One ';'-joined string is what
// crosses the network (CharacterAppearance.Selection) - fixed field order,
// and a short/old string parses with the missing tail as "".
public struct SidekickSelection : IEquatable<SidekickSelection>
{
    public string Head;
    public string UpperBody;
    public string LowerBody;
    public string BodyShape;
    public string ColorSpecies;
    public string ColorOutfits;
    public string ColorAttachments;
    public string ColorMaterials;
    public string ColorElements;

    public const int FieldCount = 9;

    public string ToJoined()
    {
        return string.Join(";", Head ?? "", UpperBody ?? "", LowerBody ?? "", BodyShape ?? "",
            ColorSpecies ?? "", ColorOutfits ?? "", ColorAttachments ?? "", ColorMaterials ?? "", ColorElements ?? "");
    }

    public static SidekickSelection Parse(string joined)
    {
        string[] parts = (joined ?? "").Split(';');
        return new SidekickSelection
        {
            Head = At(parts, 0),
            UpperBody = At(parts, 1),
            LowerBody = At(parts, 2),
            BodyShape = At(parts, 3),
            ColorSpecies = At(parts, 4),
            ColorOutfits = At(parts, 5),
            ColorAttachments = At(parts, 6),
            ColorMaterials = At(parts, 7),
            ColorElements = At(parts, 8),
        };
    }

    private static string At(string[] parts, int index) => index < parts.Length ? parts[index] : "";

    public static SidekickSelection FromProfile(PlayerProfile profile)
    {
        return new SidekickSelection
        {
            Head = profile.SidekickHeadPreset,
            UpperBody = profile.SidekickUpperBodyPreset,
            LowerBody = profile.SidekickLowerBodyPreset,
            BodyShape = profile.SidekickBodyShapePreset,
            ColorSpecies = profile.SidekickColorSpecies,
            ColorOutfits = profile.SidekickColorOutfits,
            ColorAttachments = profile.SidekickColorAttachments,
            ColorMaterials = profile.SidekickColorMaterials,
            ColorElements = profile.SidekickColorElements,
        };
    }

    public bool Equals(SidekickSelection other) => ToJoined() == other.ToJoined();
    public override bool Equals(object obj) => obj is SidekickSelection other && Equals(other);
    public override int GetHashCode() => ToJoined().GetHashCode();
}
