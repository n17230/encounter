using System;
using System.Collections.Generic;
using Synty.SidekickCharacters.Database;
using Synty.SidekickCharacters.Database.DTO;
using Synty.SidekickCharacters.Enums;

// Everything the Appearance panel can offer, read once from the Sidekick
// database (client/Editor only - the DB is never opened on a headless
// server). Mirrors Sidekick's own Presets tab: part presets per group, body
// shape presets, one color preset list per color group. Species is pinned
// to Human for the head (the base character stays human), while Upper/
// Lower Body presets come from every installed species so other packs'
// outfits can be worn on it - all parts share the SK_BaseModel skeleton.
public class SidekickPresetCatalog
{
    public const string HumanSpeciesName = "Human";
    public const string UnrestrictedSpeciesName = "Unrestricted";

    private readonly Dictionary<PartGroup, List<string>> partNames = new Dictionary<PartGroup, List<string>>();
    private readonly Dictionary<PartGroup, Dictionary<string, SidekickPartPreset>> partPresets = new Dictionary<PartGroup, Dictionary<string, SidekickPartPreset>>();
    private readonly List<string> bodyShapeNames = new List<string>();
    private readonly Dictionary<string, SidekickBodyShapePreset> bodyShapes = new Dictionary<string, SidekickBodyShapePreset>();
    private readonly Dictionary<ColorGroup, List<string>> colorNames = new Dictionary<ColorGroup, List<string>>();
    private readonly Dictionary<ColorGroup, Dictionary<string, SidekickColorPreset>> colorPresets = new Dictionary<ColorGroup, Dictionary<string, SidekickColorPreset>>();

    public SidekickPresetCatalog(DatabaseManager db)
    {
        int humanId = SpeciesId(db, HumanSpeciesName);
        int unrestrictedId = SpeciesId(db, UnrestrictedSpeciesName);

        foreach (PartGroup group in Enum.GetValues(typeof(PartGroup)))
        {
            // GetAllByGroup already drops presets whose part files are missing.
            IEnumerable<SidekickPartPreset> presets = SidekickPartPreset.GetAllByGroup(db, group);
            if (group == PartGroup.Head) presets = FilterBySpecies(presets, p => p.PtrSpecies, humanId, unrestrictedId);
            Index(group, presets);
        }

        foreach (SidekickBodyShapePreset preset in SidekickBodyShapePreset.GetAll(db))
        {
            if (string.IsNullOrEmpty(preset.Name) || bodyShapes.ContainsKey(preset.Name)) continue;
            bodyShapes[preset.Name] = preset;
            bodyShapeNames.Add(preset.Name);
        }

        foreach (ColorGroup group in Enum.GetValues(typeof(ColorGroup)))
        {
            List<string> names = new List<string>();
            Dictionary<string, SidekickColorPreset> byName = new Dictionary<string, SidekickColorPreset>();
            foreach (SidekickColorPreset preset in FilterBySpecies(SidekickColorPreset.GetAllByColorGroup(db, group), p => p.PtrSpecies, humanId, unrestrictedId))
            {
                if (string.IsNullOrEmpty(preset.Name) || byName.ContainsKey(preset.Name)) continue;
                byName[preset.Name] = preset;
                names.Add(preset.Name);
            }
            colorNames[group] = names;
            colorPresets[group] = byName;
        }
    }

    private static int SpeciesId(DatabaseManager db, string name)
    {
        SidekickSpecies species = SidekickSpecies.GetByName(db, name);
        return species != null ? species.ID : -1;
    }

    private void Index(PartGroup group, IEnumerable<SidekickPartPreset> presets)
    {
        List<string> names = new List<string>();
        Dictionary<string, SidekickPartPreset> byName = new Dictionary<string, SidekickPartPreset>();
        foreach (SidekickPartPreset preset in presets)
        {
            if (string.IsNullOrEmpty(preset.Name) || byName.ContainsKey(preset.Name)) continue;
            byName[preset.Name] = preset;
            names.Add(preset.Name);
        }
        partNames[group] = names;
        partPresets[group] = byName;
    }

    // Pure: keeps items whose species is Human or Unrestricted. A missing
    // species (-1) never matches, so an absent "Unrestricted" row simply
    // contributes nothing rather than letting everything through.
    public static IEnumerable<T> FilterBySpecies<T>(IEnumerable<T> items, Func<T, int> speciesOf, int humanId, int unrestrictedId)
    {
        foreach (T item in items)
        {
            int species = speciesOf(item);
            if (species == humanId || species == unrestrictedId) yield return item;
        }
    }

    public IReadOnlyList<string> PartPresetNames(PartGroup group) => partNames[group];
    public IReadOnlyList<string> BodyShapeNames => bodyShapeNames;
    public IReadOnlyList<string> ColorPresetNames(ColorGroup group) => colorNames[group];

    public SidekickPartPreset GetPartPreset(PartGroup group, string name)
    {
        return !string.IsNullOrEmpty(name) && partPresets[group].TryGetValue(name, out SidekickPartPreset preset) ? preset : null;
    }

    public SidekickBodyShapePreset GetBodyShape(string name)
    {
        return !string.IsNullOrEmpty(name) && bodyShapes.TryGetValue(name, out SidekickBodyShapePreset preset) ? preset : null;
    }

    public SidekickColorPreset GetColorPreset(ColorGroup group, string name)
    {
        return !string.IsNullOrEmpty(name) && colorPresets[group].TryGetValue(name, out SidekickColorPreset preset) ? preset : null;
    }

    public SidekickSelection Resolve(SidekickSelection selection)
    {
        return Resolve(selection, partNames[PartGroup.Head], partNames[PartGroup.UpperBody], partNames[PartGroup.LowerBody],
            bodyShapeNames, group => colorNames[group]);
    }

    // Pure: a part/body-shape name that's empty or no longer exists (a
    // renamed preset, a pack that was removed) becomes the group's first
    // entry so a character can always be built; a color name that no longer
    // exists becomes "" (base material), never a substitute color.
    public static SidekickSelection Resolve(SidekickSelection selection, IReadOnlyList<string> heads, IReadOnlyList<string> uppers,
        IReadOnlyList<string> lowers, IReadOnlyList<string> bodyShapes, Func<ColorGroup, IReadOnlyList<string>> colors)
    {
        return new SidekickSelection
        {
            Head = FirstIfUnknown(selection.Head, heads),
            UpperBody = FirstIfUnknown(selection.UpperBody, uppers),
            LowerBody = FirstIfUnknown(selection.LowerBody, lowers),
            BodyShape = FirstIfUnknown(selection.BodyShape, bodyShapes),
            ColorSpecies = EmptyIfUnknown(selection.ColorSpecies, colors(ColorGroup.Species)),
            ColorOutfits = EmptyIfUnknown(selection.ColorOutfits, colors(ColorGroup.Outfits)),
            ColorAttachments = EmptyIfUnknown(selection.ColorAttachments, colors(ColorGroup.Attachments)),
            ColorMaterials = EmptyIfUnknown(selection.ColorMaterials, colors(ColorGroup.Materials)),
            ColorElements = EmptyIfUnknown(selection.ColorElements, colors(ColorGroup.Elements)),
        };
    }

    private static string FirstIfUnknown(string name, IReadOnlyList<string> known)
    {
        if (!string.IsNullOrEmpty(name) && Contains(known, name)) return name;
        return known.Count > 0 ? known[0] : "";
    }

    private static string EmptyIfUnknown(string name, IReadOnlyList<string> known)
    {
        return !string.IsNullOrEmpty(name) && Contains(known, name) ? name : "";
    }

    private static bool Contains(IReadOnlyList<string> list, string name)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == name) return true;
        }
        return false;
    }
}
