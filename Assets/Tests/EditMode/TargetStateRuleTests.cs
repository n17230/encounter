using NUnit.Framework;

public class TargetStateRuleTests
{
    [Test]
    public void NormalAbilityOnLivingTargetIsAllowed()
    {
        Assert.IsNull(TargetStateRule.Check(resurrectAbility: false, targetAlive: true, targetIsPlayer: true));
        Assert.IsNull(TargetStateRule.Check(resurrectAbility: false, targetAlive: true, targetIsPlayer: false));
    }

    [Test]
    public void NormalAbilityOnCorpseIsRejected()
    {
        Assert.AreEqual("Target is dead", TargetStateRule.Check(resurrectAbility: false, targetAlive: false, targetIsPlayer: true));
    }

    // A dead mob (in its despawn window) is a corpse like any other to a
    // normal ability - the player/mob distinction only matters to Resurrect.
    [Test]
    public void NormalAbilityOnDeadMobIsRejectedAsDead()
    {
        Assert.AreEqual("Target is dead", TargetStateRule.Check(resurrectAbility: false, targetAlive: false, targetIsPlayer: false));
    }

    [Test]
    public void ResurrectOnDeadPlayerIsAllowed()
    {
        Assert.IsNull(TargetStateRule.Check(resurrectAbility: true, targetAlive: false, targetIsPlayer: true));
    }

    [Test]
    public void ResurrectOnLivingPlayerIsRejected()
    {
        Assert.AreEqual("Target is not dead", TargetStateRule.Check(resurrectAbility: true, targetAlive: true, targetIsPlayer: true));
    }

    [Test]
    public void ResurrectOnDeadMobIsRejected()
    {
        Assert.AreEqual("Can only resurrect players", TargetStateRule.Check(resurrectAbility: true, targetAlive: false, targetIsPlayer: false));
    }

    // Pins the check order: a living mob is rejected for being a mob, not
    // for being alive - the not-a-player check comes before the not-dead one.
    [Test]
    public void ResurrectOnLivingMobIsRejectedForBeingAMob()
    {
        Assert.AreEqual("Can only resurrect players", TargetStateRule.Check(resurrectAbility: true, targetAlive: true, targetIsPlayer: false));
    }
}
