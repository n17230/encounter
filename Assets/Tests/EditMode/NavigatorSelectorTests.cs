using NUnit.Framework;

public class NavigatorSelectorTests
{
    private static readonly ulong[] Four = { 10, 20, 30, 40 };

    [Test]
    public void RollZeroPicksTheFirst()
    {
        Assert.AreEqual(10ul, NavigatorSelector.Pick(Four, 0f));
    }

    [Test]
    public void RollJustUnderOnePicksTheLast()
    {
        Assert.AreEqual(40ul, NavigatorSelector.Pick(Four, 0.999f));
    }

    [Test]
    public void RollHalfOfFourPicksIndexTwo()
    {
        Assert.AreEqual(30ul, NavigatorSelector.Pick(Four, 0.5f));
    }

    // The index is floor(roll * count), not round - 0.7 * 4 = 2.8 lands on
    // index 2, 0.26 * 4 = 1.04 on index 1.
    [Test]
    public void IndexIsFlooredNotRounded()
    {
        Assert.AreEqual(30ul, NavigatorSelector.Pick(Four, 0.7f));
        Assert.AreEqual(20ul, NavigatorSelector.Pick(Four, 0.26f));
    }

    // "Nobody" must never collide with a real id - NGO's server id is 0.
    [Test]
    public void NoneIsMaxValueAndNotTheServerId()
    {
        Assert.AreEqual(ulong.MaxValue, NavigatorSelector.None);
        Assert.AreNotEqual(0ul, NavigatorSelector.None);
    }

    [Test]
    public void SingleCandidateIsPickedWhateverTheRoll()
    {
        Assert.AreEqual(7ul, NavigatorSelector.Pick(new ulong[] { 7 }, 0f));
        Assert.AreEqual(7ul, NavigatorSelector.Pick(new ulong[] { 7 }, 0.75f));
    }

    [Test]
    public void NoCandidatesGivesNone()
    {
        Assert.AreEqual(NavigatorSelector.None, NavigatorSelector.Pick(new ulong[0], 0.3f));
        Assert.AreEqual(NavigatorSelector.None, NavigatorSelector.Pick(null, 0.3f));
    }

    // Random.value can return exactly 1.0 - that must land on the last
    // candidate, not one past the end.
    [Test]
    public void RollOfExactlyOneClampsToTheLast()
    {
        Assert.AreEqual(40ul, NavigatorSelector.Pick(Four, 1f));
    }
}
