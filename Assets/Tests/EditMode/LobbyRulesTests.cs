using NUnit.Framework;

public class LobbyRulesTests
{
    private static LobbyReadiness Entry(ulong id, bool admitted, bool ready) => new LobbyReadiness(id, admitted, ready);

    [Test]
    public void AnEmptyLobbyIsNeverReady()
    {
        Assert.IsFalse(LobbyRules.AllReady(new LobbyReadiness[0]));
    }

    [Test]
    public void OneReadyAdmittedPlayerIsAllReady()
    {
        Assert.IsTrue(LobbyRules.AllReady(new[] { Entry(1, true, true) }));
    }

    [Test]
    public void OneNotReadyPlayerBlocksAllReady()
    {
        Assert.IsFalse(LobbyRules.AllReady(new[] { Entry(1, true, true), Entry(2, true, false) }));
    }

    // A waiter (joined mid-pick, not admitted) doesn't count either way -
    // their unready state doesn't block the team, and their ready press
    // alone doesn't make an otherwise-empty lobby ready.
    [Test]
    public void NonAdmittedEntriesAreIgnored()
    {
        Assert.IsTrue(LobbyRules.AllReady(new[] { Entry(1, true, true), Entry(2, false, false) }));
        Assert.IsFalse(LobbyRules.AllReady(new[] { Entry(2, false, true) }));
    }

    [Test]
    public void JoiningIsAdmittedOnlyInMainMenuOrAfterStart()
    {
        Assert.IsTrue(LobbyRules.IsAdmittedJoin(LobbyPhase.MainMenu));
        Assert.IsTrue(LobbyRules.IsAdmittedJoin(LobbyPhase.Started));
        Assert.IsFalse(LobbyRules.IsAdmittedJoin(LobbyPhase.Choice));
        Assert.IsFalse(LobbyRules.IsAdmittedJoin(LobbyPhase.Spells));
        Assert.IsFalse(LobbyRules.IsAdmittedJoin(LobbyPhase.Equipment));
    }

    [Test]
    public void NavigationFollowsTheTransitionTable()
    {
        Assert.IsTrue(LobbyRules.IsLegalNavigation(LobbyPhase.Choice, LobbyPhase.Spells));
        Assert.IsTrue(LobbyRules.IsLegalNavigation(LobbyPhase.Choice, LobbyPhase.Equipment));
        Assert.IsTrue(LobbyRules.IsLegalNavigation(LobbyPhase.Choice, LobbyPhase.MainMenu));
        Assert.IsTrue(LobbyRules.IsLegalNavigation(LobbyPhase.Spells, LobbyPhase.Choice));
        Assert.IsTrue(LobbyRules.IsLegalNavigation(LobbyPhase.Equipment, LobbyPhase.Choice));

        Assert.IsFalse(LobbyRules.IsLegalNavigation(LobbyPhase.Choice, LobbyPhase.Choice));
        Assert.IsFalse(LobbyRules.IsLegalNavigation(LobbyPhase.Spells, LobbyPhase.Spells));
        Assert.IsFalse(LobbyRules.IsLegalNavigation(LobbyPhase.Equipment, LobbyPhase.Spells));
        Assert.IsFalse(LobbyRules.IsLegalNavigation(LobbyPhase.Equipment, LobbyPhase.MainMenu));
        Assert.IsFalse(LobbyRules.IsLegalNavigation(LobbyPhase.Equipment, LobbyPhase.Equipment));
        Assert.IsFalse(LobbyRules.IsLegalNavigation(LobbyPhase.Spells, LobbyPhase.Equipment));
        Assert.IsFalse(LobbyRules.IsLegalNavigation(LobbyPhase.Spells, LobbyPhase.MainMenu));
        Assert.IsFalse(LobbyRules.IsLegalNavigation(LobbyPhase.Choice, LobbyPhase.Started));
        Assert.IsFalse(LobbyRules.IsLegalNavigation(LobbyPhase.MainMenu, LobbyPhase.Choice));
        Assert.IsFalse(LobbyRules.IsLegalNavigation(LobbyPhase.Started, LobbyPhase.Choice));
    }

    [Test]
    public void ANonNavigatorLeavingChangesNothing()
    {
        NavigatorOutcome outcome = LobbyRules.ResolveNavigatorAfterLeave(2, 1, new ulong[] { 1, 3 }, 0.9f, out ulong navigator);
        Assert.AreEqual(NavigatorOutcome.Unchanged, outcome);
        Assert.AreEqual(1ul, navigator);
    }

    [Test]
    public void TheNavigatorLeavingRepicksAmongTheRemainingAdmitted()
    {
        NavigatorOutcome outcome = LobbyRules.ResolveNavigatorAfterLeave(1, 1, new ulong[] { 2, 3 }, 0.9f, out ulong navigator);
        Assert.AreEqual(NavigatorOutcome.Repicked, outcome);
        Assert.AreEqual(3ul, navigator);
    }

    [Test]
    public void TheNavigatorLeavingWithNobodyAdmittedLeftMeansBackToMainMenu()
    {
        NavigatorOutcome outcome = LobbyRules.ResolveNavigatorAfterLeave(1, 1, new ulong[0], 0.5f, out ulong navigator);
        Assert.AreEqual(NavigatorOutcome.NoneLeft, outcome);
        Assert.AreEqual(NavigatorSelector.None, navigator);
    }
}
