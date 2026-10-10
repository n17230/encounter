using System.Collections.Generic;
using NUnit.Framework;

public class NavAgentTypeSelectorTests
{
    private const int Humanoid = 0;
    private const int Ogre = 7;

    private static readonly NavAgentTypeSize HumanoidType = new NavAgentTypeSize { Id = Humanoid, Radius = 0.5f, Height = 2f };
    private static readonly NavAgentTypeSize OgreType = new NavAgentTypeSize { Id = Ogre, Radius = 1f, Height = 4f };

    [Test]
    public void StandardSizedMobUsesTheHumanoidType()
    {
        var types = new List<NavAgentTypeSize> { HumanoidType, OgreType };

        Assert.AreEqual(Humanoid, NavAgentTypeSelector.Select(0.5f, 2f, types, fallbackId: -1));
    }

    // A mob smaller than the smallest type still uses it - "smallest that
    // fits", not "closest match" (a 0.8-scale goblin is not an Ogre).
    [Test]
    public void SmallerMobStillUsesTheSmallestType()
    {
        var types = new List<NavAgentTypeSize> { OgreType, HumanoidType };

        Assert.AreEqual(Humanoid, NavAgentTypeSelector.Select(0.4f, 1.6f, types, fallbackId: -1));
    }

    [Test]
    public void DoubleScaledMobUsesTheLargerType()
    {
        var types = new List<NavAgentTypeSize> { HumanoidType, OgreType };

        Assert.AreEqual(Ogre, NavAgentTypeSelector.Select(1f, 4f, types, fallbackId: -1));
    }

    // Both dimensions must fit: wide-but-short or tall-but-narrow mobs
    // can't go on a type that's too small in either.
    [Test]
    public void NeedsBothRadiusAndHeightToFit()
    {
        var types = new List<NavAgentTypeSize> { HumanoidType, OgreType };

        Assert.AreEqual(Ogre, NavAgentTypeSelector.Select(0.5f, 3f, types, fallbackId: -1), "too tall for Humanoid");
        Assert.AreEqual(Ogre, NavAgentTypeSelector.Select(0.9f, 2f, types, fallbackId: -1), "too wide for Humanoid");
    }

    [Test]
    public void MobLargerThanEveryTypeUsesTheLargest()
    {
        var types = new List<NavAgentTypeSize> { OgreType, HumanoidType };

        Assert.AreEqual(Ogre, NavAgentTypeSelector.Select(2f, 8f, types, fallbackId: -1));
    }

    [Test]
    public void NoTypesReturnsTheFallback()
    {
        Assert.AreEqual(-1, NavAgentTypeSelector.Select(0.5f, 2f, new List<NavAgentTypeSize>(), fallbackId: -1));
    }
}
