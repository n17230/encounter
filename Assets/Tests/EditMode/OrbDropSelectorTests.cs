using NUnit.Framework;

public class OrbDropSelectorTests
{
    [Test]
    public void RollBelowManaChanceReturnsMana()
    {
        Assert.AreEqual(OrbDropResult.Mana, OrbDropSelector.Select(0.03f, manaChance: 0.04f, healthChance: 0.06f));
    }

    [Test]
    public void RollBetweenManaAndCombinedChanceReturnsHealth()
    {
        Assert.AreEqual(OrbDropResult.Health, OrbDropSelector.Select(0.08f, manaChance: 0.04f, healthChance: 0.06f));
    }

    [Test]
    public void RollAboveCombinedChanceReturnsNone()
    {
        Assert.AreEqual(OrbDropResult.None, OrbDropSelector.Select(0.5f, manaChance: 0.04f, healthChance: 0.06f));
    }

    // The boundary exactly at manaChance belongs to Health, not Mana - the
    // mana check is strictly "<", so a roll equal to the threshold falls
    // through to the health check instead.
    [Test]
    public void RollExactlyAtManaChanceReturnsHealth()
    {
        Assert.AreEqual(OrbDropResult.Health, OrbDropSelector.Select(0.04f, manaChance: 0.04f, healthChance: 0.06f));
    }

    // Same reasoning at the outer boundary: a roll equal to the combined
    // chance is not "< combined", so it falls through to None.
    [Test]
    public void RollExactlyAtCombinedChanceReturnsNone()
    {
        Assert.AreEqual(OrbDropResult.None, OrbDropSelector.Select(0.10f, manaChance: 0.04f, healthChance: 0.06f));
    }
}
