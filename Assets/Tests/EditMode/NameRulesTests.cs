using NUnit.Framework;

public class NameRulesTests
{
    private static readonly string[] NoOthers = new string[0];

    [Test]
    public void EmptyAndWhitespaceOnlyNamesAreRejected()
    {
        Assert.IsNotNull(NameRules.Validate("", NoOthers, out _));
        Assert.IsNotNull(NameRules.Validate("   ", NoOthers, out _));
        Assert.IsNotNull(NameRules.Validate(null, NoOthers, out _));
    }

    [Test]
    public void ASingleCharacterIsAccepted()
    {
        Assert.IsNull(NameRules.Validate("a", NoOthers, out string normalized));
        Assert.AreEqual("a", normalized);
    }

    [Test]
    public void SixteenCharactersAreAcceptedAndSeventeenAreNot()
    {
        Assert.IsNull(NameRules.Validate(new string('x', 16), NoOthers, out _));
        Assert.IsNotNull(NameRules.Validate(new string('x', 17), NoOthers, out _));
    }

    [Test]
    public void SurroundingWhitespaceIsTrimmedOff()
    {
        Assert.IsNull(NameRules.Validate(" Bob ", NoOthers, out string normalized));
        Assert.AreEqual("Bob", normalized);
    }

    [Test]
    public void UniquenessIsCaseInsensitive()
    {
        Assert.IsNotNull(NameRules.Validate("bob", new[] { "BOB" }, out _));
        Assert.IsNull(NameRules.Validate("bob", new[] { "alice" }, out _));
    }

    // Other names are compared trimmed too, so a stored name can't dodge
    // the uniqueness check through surrounding whitespace.
    [Test]
    public void UniquenessTrimsTheOtherNames()
    {
        Assert.IsNotNull(NameRules.Validate("bob", new[] { " Bob " }, out _));
    }

    // 16 code points is within the character limit, but 16 four-byte
    // characters are 64 UTF-8 bytes - past what the FixedString64Bytes the
    // name is stored in can hold, so it's the BYTE rule that rejects it,
    // not the character count.
    [Test]
    public void SixteenFourByteCharactersExceedTheStorageLimit()
    {
        string sixteenEmoji = string.Concat(System.Linq.Enumerable.Repeat("\U0001F600", 16));
        string reason = NameRules.Validate(sixteenEmoji, NoOthers, out _);
        Assert.IsNotNull(reason);
        StringAssert.Contains("special characters", reason);
        StringAssert.DoesNotContain("at most", reason);
        // The same count of 3-byte characters fits (48 bytes), as do 10
        // emoji (10 code points, 40 bytes).
        Assert.IsNull(NameRules.Validate(new string('€', 16), NoOthers, out _));
        Assert.IsNull(NameRules.Validate(string.Concat(System.Linq.Enumerable.Repeat("\U0001F600", 10)), NoOthers, out _));
    }

    // Exact storage boundary: 61 UTF-8 bytes (FixedString64Bytes' capacity)
    // is the last accepted size. 15 three-byte characters + 16 one-byte
    // characters would be 31 code points, so the boundary is built from
    // 16 code points: 15 x 4 bytes + 1 x 1 byte = 61, and 15 x 4 + 1 x 2 = 62.
    [Test]
    public void SixtyOneBytesAcceptedSixtyTwoRejected()
    {
        string fifteenEmoji = string.Concat(System.Linq.Enumerable.Repeat("\U0001F600", 15));
        Assert.IsNull(NameRules.Validate(fifteenEmoji + "a", NoOthers, out _));
        Assert.IsNotNull(NameRules.Validate(fifteenEmoji + "é", NoOthers, out _));
    }

    // Not tested here: "your own current name isn't taken" is a property of
    // the caller, not of Validate - LobbyState.OtherNames builds the list
    // from every entry except the sender's.

    [Test]
    public void IsWellFormedOnlyChecksShapeNotUniqueness()
    {
        Assert.IsTrue(NameRules.IsWellFormed("Bob"));
        Assert.IsFalse(NameRules.IsWellFormed(" "));
        Assert.IsFalse(NameRules.IsWellFormed(new string('x', 17)));
    }
}
