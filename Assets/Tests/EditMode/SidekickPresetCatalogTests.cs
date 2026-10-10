using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synty.SidekickCharacters.Enums;

public class SidekickPresetCatalogTests
{
    private const int Human = 2;
    private const int Unrestricted = 1;
    private const int Goblin = 3;

    [Test]
    public void FilterBySpeciesKeepsHumanAndUnrestrictedOnly()
    {
        var items = new List<(string Name, int Species)>
        {
            ("human head", Human), ("goblin head", Goblin), ("any head", Unrestricted),
        };

        List<string> kept = SidekickPresetCatalog.FilterBySpecies(items, i => i.Species, Human, Unrestricted).Select(i => i.Name).ToList();

        CollectionAssert.AreEqual(new[] { "human head", "any head" }, kept);
    }

    // A species that isn't in the database resolves to -1 - that must never
    // accidentally match a real row (nothing has species -1), so a missing
    // "Unrestricted" row contributes nothing rather than everything.
    [Test]
    public void MissingSpeciesIdMatchesNothing()
    {
        var items = new List<(string Name, int Species)> { ("goblin", Goblin), ("human", Human) };

        List<string> kept = SidekickPresetCatalog.FilterBySpecies(items, i => i.Species, Human, -1).Select(i => i.Name).ToList();

        CollectionAssert.AreEqual(new[] { "human" }, kept);
    }

    private static readonly string[] Heads = { "Head 1", "Head 2" };
    private static readonly string[] Uppers = { "Upper 1" };
    private static readonly string[] Lowers = { "Lower 1", "Lower 2" };
    private static readonly string[] Shapes = { "Average", "Heavy" };
    private static readonly Dictionary<ColorGroup, string[]> Colors = new Dictionary<ColorGroup, string[]>
    {
        { ColorGroup.Species, new[] { "Pale", "Tan" } },
        { ColorGroup.Outfits, new[] { "Red" } },
        { ColorGroup.Attachments, new string[0] },
        { ColorGroup.Materials, new[] { "Iron" } },
        { ColorGroup.Elements, new string[0] },
    };

    private static SidekickSelection Resolve(SidekickSelection selection)
    {
        return SidekickPresetCatalog.Resolve(selection, Heads, Uppers, Lowers, Shapes, group => Colors[group]);
    }

    [Test]
    public void KnownNamesAreKeptAsIs()
    {
        SidekickSelection selection = new SidekickSelection
        {
            Head = "Head 2", UpperBody = "Upper 1", LowerBody = "Lower 2", BodyShape = "Heavy",
            ColorSpecies = "Tan", ColorOutfits = "Red", ColorMaterials = "Iron",
        };

        Assert.AreEqual(selection, Resolve(selection));
    }

    // Empty or renamed/removed part and body-shape names fall back to the
    // group's first entry so a character can always be built.
    [Test]
    public void EmptyOrUnknownPartsFallBackToFirstEntry()
    {
        SidekickSelection resolved = Resolve(new SidekickSelection { Head = "", UpperBody = "gone", LowerBody = "Lower 2", BodyShape = "nope" });

        Assert.AreEqual("Head 1", resolved.Head);
        Assert.AreEqual("Upper 1", resolved.UpperBody);
        Assert.AreEqual("Lower 2", resolved.LowerBody);
        Assert.AreEqual("Average", resolved.BodyShape);
    }

    // Colors are optional: an unknown one becomes "" (base material), never
    // a substitute color, and an empty one stays empty.
    [Test]
    public void UnknownColorsBecomeEmptyNotSubstituted()
    {
        SidekickSelection resolved = Resolve(new SidekickSelection { ColorSpecies = "gone", ColorOutfits = "", ColorAttachments = "anything" });

        Assert.AreEqual("", resolved.ColorSpecies);
        Assert.AreEqual("", resolved.ColorOutfits);
        Assert.AreEqual("", resolved.ColorAttachments);
    }

    [Test]
    public void EmptyGroupResolvesToEmptyName()
    {
        SidekickSelection resolved = SidekickPresetCatalog.Resolve(default, new string[0], Uppers, Lowers, Shapes, group => Colors[group]);

        Assert.AreEqual("", resolved.Head);
        Assert.AreEqual("Upper 1", resolved.UpperBody);
    }
}
