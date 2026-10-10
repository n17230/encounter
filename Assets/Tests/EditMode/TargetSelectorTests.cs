using System.Collections.Generic;
using NUnit.Framework;

public class TargetSelectorTests
{
    private static TargetCandidate<string> Candidate(string name, float threat, float distance, float healing = 0f) =>
        new TargetCandidate<string> { Subject = name, Threat = threat, Distance = distance, Healing = healing };

    private static readonly List<TargetCandidate<string>> Party = new List<TargetCandidate<string>>
    {
        Candidate("tank", threat: 300f, distance: 5f),
        Candidate("mage", threat: 500f, distance: 20f),
        Candidate("healer", threat: 0f, distance: 12f),
        Candidate("rogue", threat: 500f, distance: 8f),
    };

    // Party, plus the healer has healing credit on this mob.
    private static readonly List<TargetCandidate<string>> HealingParty = new List<TargetCandidate<string>>
    {
        Candidate("tank", threat: 300f, distance: 5f),
        Candidate("mage", threat: 500f, distance: 20f),
        Candidate("healer", threat: 0f, distance: 12f, healing: 120f),
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
    public void MostHealingPicksWhoeverHealedTheMost()
    {
        Assert.AreEqual("healer", TargetSelector.Select(HealingParty, TargetingMode.MostHealing, hasThreatTable: true));
    }

    [Test]
    public void MostHealingBreaksTiesByDistance()
    {
        List<TargetCandidate<string>> tied = new List<TargetCandidate<string>>
        {
            Candidate("tank", threat: 300f, distance: 5f),
            Candidate("mage", threat: 500f, distance: 20f, healing: 120f),
            Candidate("healer", threat: 0f, distance: 12f, healing: 120f),
            Candidate("rogue", threat: 500f, distance: 8f),
        };
        Assert.AreEqual("healer", TargetSelector.Select(tied, TargetingMode.MostHealing, hasThreatTable: true));
    }

    [Test]
    public void MostHealingWithNoHealingCreditIsHighestThreat()
    {
        // Not nearest ("tank") - exactly what HighestThreat would pick.
        Assert.AreEqual("rogue", TargetSelector.Select(Party, TargetingMode.MostHealing, hasThreatTable: true));
    }

    [Test]
    public void MostHealingFallsBackToNearestWithoutAThreatTable()
    {
        Assert.AreEqual("tank", TargetSelector.Select(HealingParty, TargetingMode.MostHealing, hasThreatTable: false));
        Assert.AreEqual("tank", TargetSelector.Select(Party, TargetingMode.MostHealing, hasThreatTable: false));
    }

    // The winner is never at index 0 in the fixtures above; these pin the
    // loop bounds - a scan that skips index 1 or stops before the last entry
    // would miss the healer.
    [Test]
    public void MostHealingFindsTheWinnerAtTheSecondIndex()
    {
        List<TargetCandidate<string>> party = new List<TargetCandidate<string>>
        {
            Candidate("tank", threat: 300f, distance: 5f),
            Candidate("healer", threat: 0f, distance: 12f, healing: 120f),
            Candidate("mage", threat: 500f, distance: 20f),
            Candidate("rogue", threat: 500f, distance: 8f),
        };
        Assert.AreEqual("healer", TargetSelector.Select(party, TargetingMode.MostHealing, hasThreatTable: true));
    }

    [Test]
    public void MostHealingFindsTheWinnerAtTheLastIndex()
    {
        List<TargetCandidate<string>> party = new List<TargetCandidate<string>>
        {
            Candidate("tank", threat: 300f, distance: 5f),
            Candidate("mage", threat: 500f, distance: 20f),
            Candidate("rogue", threat: 500f, distance: 8f),
            Candidate("healer", threat: 0f, distance: 12f, healing: 120f),
        };
        Assert.AreEqual("healer", TargetSelector.Select(party, TargetingMode.MostHealing, hasThreatTable: true));
    }

    // With no healing credit anywhere, MostHealing must be indistinguishable
    // from HighestThreat - with or without a table.
    [Test]
    public void MostHealingWithoutCreditMatchesHighestThreatExactly()
    {
        Assert.AreEqual(TargetSelector.Select(Party, TargetingMode.HighestThreat, hasThreatTable: true),
            TargetSelector.Select(Party, TargetingMode.MostHealing, hasThreatTable: true));
        Assert.AreEqual(TargetSelector.Select(Party, TargetingMode.HighestThreat, hasThreatTable: false),
            TargetSelector.Select(Party, TargetingMode.MostHealing, hasThreatTable: false));
    }

    // Equal healing AND equal distance: ByHealing only replaces the best on
    // a strictly closer (<) distance, so the first-listed candidate wins.
    [Test]
    public void MostHealingFullTieKeepsTheFirstListed()
    {
        List<TargetCandidate<string>> party = new List<TargetCandidate<string>>
        {
            Candidate("tank", threat: 300f, distance: 5f),
            Candidate("mage", threat: 500f, distance: 12f, healing: 120f),
            Candidate("healer", threat: 0f, distance: 12f, healing: 120f),
        };
        Assert.AreEqual("mage", TargetSelector.Select(party, TargetingMode.MostHealing, hasThreatTable: true));
    }

    [Test]
    public void NoCandidatesGivesNoTarget()
    {
        Assert.IsNull(TargetSelector.Select(new List<TargetCandidate<string>>(), TargetingMode.HighestThreat, true));
        Assert.IsNull(TargetSelector.Select<string>(null, TargetingMode.Proximity, true));
    }
}
