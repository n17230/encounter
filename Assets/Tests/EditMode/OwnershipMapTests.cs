using NUnit.Framework;

public class OwnershipMapTests
{
    [Test]
    public void AnIdSomeoneElseHoldsCannotBeClaimed()
    {
        OwnershipMap map = new OwnershipMap();
        Assert.IsTrue(map.TryClaim("x", 1));
        Assert.IsFalse(map.TryClaim("x", 2));
    }

    // The lobby confirm and the spawn-time sync both claim the same Ids for
    // the same client - the second claim must be a harmless no-op.
    [Test]
    public void ReclaimingYourOwnIdSucceeds()
    {
        OwnershipMap map = new OwnershipMap();
        Assert.IsTrue(map.TryClaim("x", 1));
        Assert.IsTrue(map.TryClaim("x", 1));
        Assert.IsTrue(map.IsOwnedBy("x", 1));
    }

    [Test]
    public void AReleasedIdCanBeClaimedByAnotherClient()
    {
        OwnershipMap map = new OwnershipMap();
        map.TryClaim("x", 1);
        map.Release("x", 1);
        Assert.IsTrue(map.TryClaim("x", 2));
    }

    [Test]
    public void ReleaseByANonOwnerIsANoOp()
    {
        OwnershipMap map = new OwnershipMap();
        map.TryClaim("x", 1);
        map.Release("x", 2);
        Assert.IsTrue(map.IsOwnedBy("x", 1));
        Assert.IsFalse(map.TryClaim("x", 2));
    }

    [Test]
    public void ReleaseAllExceptKeepsOnlyTheNamedIds()
    {
        OwnershipMap map = new OwnershipMap();
        map.TryClaim("a", 1);
        map.TryClaim("b", 1);
        map.TryClaim("c", 2);

        map.ReleaseAllExcept(1, new[] { "b" });

        Assert.IsFalse(map.IsOwnedBy("a", 1));
        Assert.IsTrue(map.IsOwnedBy("b", 1));
        Assert.IsTrue(map.IsOwnedBy("c", 2)); // another client's claim is untouched
    }

    // ReleaseAllExcept reuses one scratch list across calls - a sequence of
    // releases and re-claims on the same map must not see leftovers from the
    // previous call.
    [Test]
    public void RepeatedReleaseAllExceptCallsDoNotLeakScratchState()
    {
        OwnershipMap map = new OwnershipMap();
        map.TryClaim("a", 1);
        map.TryClaim("b", 1);

        map.ReleaseAllExcept(1, new[] { "b" });
        Assert.IsTrue(map.TryClaim("a", 1));
        map.ReleaseAllExcept(1, new[] { "a", "b" });

        Assert.IsTrue(map.IsOwnedBy("a", 1));
        Assert.IsTrue(map.IsOwnedBy("b", 1));
    }

    [Test]
    public void ReleaseAllFreesEverythingTheClientHeld()
    {
        OwnershipMap map = new OwnershipMap();
        map.TryClaim("a", 1);
        map.TryClaim("b", 1);

        map.ReleaseAll(1);

        Assert.IsTrue(map.TryClaim("a", 2));
        Assert.IsTrue(map.TryClaim("b", 2));
    }
}
