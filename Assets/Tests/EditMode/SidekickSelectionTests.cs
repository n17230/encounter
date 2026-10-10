using NUnit.Framework;

public class SidekickSelectionTests
{
    private static SidekickSelection Full() => new SidekickSelection
    {
        Head = "Head A", UpperBody = "Upper B", LowerBody = "Lower C", BodyShape = "Shape D",
        ColorSpecies = "Skin 1", ColorOutfits = "Outfit 2", ColorAttachments = "Att 3", ColorMaterials = "Mat 4", ColorElements = "Elem 5",
    };

    [Test]
    public void JoinedRoundTripsEveryField()
    {
        SidekickSelection original = Full();

        SidekickSelection parsed = SidekickSelection.Parse(original.ToJoined());

        Assert.AreEqual(original, parsed);
        Assert.AreEqual("Lower C", parsed.LowerBody);
        Assert.AreEqual("Elem 5", parsed.ColorElements);
    }

    // A string written by an older build with fewer fields (or the empty
    // default of a fresh NetworkVariable) must parse with the missing tail
    // as "", never throw.
    [Test]
    public void ShortOrEmptyStringParsesWithEmptyTail()
    {
        SidekickSelection parsed = SidekickSelection.Parse("Head A;Upper B");
        Assert.AreEqual("Head A", parsed.Head);
        Assert.AreEqual("Upper B", parsed.UpperBody);
        Assert.AreEqual("", parsed.LowerBody);
        Assert.AreEqual("", parsed.ColorElements);

        SidekickSelection empty = SidekickSelection.Parse("");
        Assert.AreEqual("", empty.Head);
        Assert.AreEqual("", empty.BodyShape);

        Assert.AreEqual(empty, SidekickSelection.Parse(null));
    }

    // The joined string is what crosses the network and what profile.json
    // holds - its field ORDER is a wire/save format, pinned here so a
    // reordering can't silently change what an older client/profile means.
    [Test]
    public void JoinedFieldOrderIsPinned()
    {
        Assert.AreEqual("Head A;Upper B;Lower C;Shape D;Skin 1;Outfit 2;Att 3;Mat 4;Elem 5", Full().ToJoined());
    }

    [Test]
    public void NullFieldsJoinAsEmpty()
    {
        SidekickSelection selection = default;

        Assert.AreEqual(";;;;;;;;", selection.ToJoined());
        Assert.AreEqual(SidekickSelection.FieldCount, selection.ToJoined().Split(';').Length);
    }

    [Test]
    public void EqualityComparesEveryField()
    {
        SidekickSelection a = Full();
        SidekickSelection b = Full();
        Assert.IsTrue(a.Equals(b));

        b.ColorMaterials = "Other";
        Assert.IsFalse(a.Equals(b));
    }
}
