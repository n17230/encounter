using System.Collections.Generic;
using NUnit.Framework;

public class TargetSelectorTests
{
    private static TargetCandidate<string> Candidate(string name, float threat, float distance) =>
        new TargetCandidate<string> { Subject = name, Threat = threat, Distance = distance };

    private static readonly List<TargetCandidate<string>> Party = new List<TargetCandidate<string>>
    {
        Candidate("tank", threat: 300f, distance: 5f),
        Candidate("mage", threat: 500f, distance: 20f),
        Candidate("healer", threat: 0f, distance: 12f),
        Candidate("rogue", threat: 500f, distance: 8f),
    };

    [Test]
    public void ProximityPicksNearestRegardlessOfThreat()
    {
        Assert.AreEqual("tank", TargetSelector.Select(Party, TargetingMode.Proximity, hasThreatTable: true));
    }

    [Test]
    public void HighestThreatBreaksTiesByDistance()
    {
        Assert.AreEqual("rogue", TargetSelector.Select(Party, TargetingMode.HighestThreat, hasThreatTable: true));
    }

    [Test]
    public void LowestThreatSinglesOutTheUntouchedPlayer()
    {
        Assert.AreEqual("healer", TargetSelector.Select(Party, TargetingMode.LowestThreat, hasThreatTable: true));
    }

    [Test]
    public void FarthestPlayerIgnoresThreat()
    {
        Assert.AreEqual("mage", TargetSelector.Select(Party, TargetingMode.FarthestPlayer, hasThreatTable: true));
    }

    [Test]
    public void ThreatModesFallBackToNearestWithoutAThreatTable()
    {
        Assert.AreEqual("tank", TargetSelector.Select(Party, TargetingMode.HighestThreat, hasThreatTable: false));
        Assert.AreEqual("tank", TargetSelector.Select(Party, TargetingMode.LowestThreat, hasThreatTable: false));
    }

    [Test]
    public void NoCandidatesGivesNoTarget()
    {
        Assert.IsNull(TargetSelector.Select(new List<TargetCandidate<string>>(), TargetingMode.HighestThreat, true));
        Assert.IsNull(TargetSelector.Select<string>(null, TargetingMode.Proximity, true));
    }
}
