using NUnit.Framework;

public class CastPushbackTrackerTests
{
    private const float Tolerance = 1e-4f;

    [Test]
    public void ThreeSecondCastShrinksPerHitThenStops()
    {
        var tracker = new CastPushbackTracker(3f);

        Assert.AreEqual(1.2f, tracker.RegisterHit(), Tolerance, "1st hit = 40%");
        Assert.AreEqual(0.6f, tracker.RegisterHit(), Tolerance, "2nd hit = 20%");
        Assert.AreEqual(0.3f, tracker.RegisterHit(), Tolerance, "3rd hit = 10%");
        Assert.AreEqual(0f, tracker.RegisterHit(), Tolerance, "4th hit adds nothing");
        Assert.AreEqual(0f, tracker.RegisterHit(), Tolerance, "5th hit adds nothing");
    }

    // A cast time that isn't a multiple of the table pins the fractions
    // themselves, not just a convenient product.
    [Test]
    public void FractionsAreExactOnASevenSecondCast()
    {
        var tracker = new CastPushbackTracker(7f);

        Assert.AreEqual(2.8f, tracker.RegisterHit(), Tolerance);
        Assert.AreEqual(1.4f, tracker.RegisterHit(), Tolerance);
        Assert.AreEqual(0.7f, tracker.RegisterHit(), Tolerance);
        Assert.AreEqual(0f, tracker.RegisterHit(), Tolerance);
    }

    [Test]
    public void InstantCastIsNeverPushedBackButHitsStillCount()
    {
        var tracker = new CastPushbackTracker(0f);

        Assert.AreEqual(0f, tracker.RegisterHit(), Tolerance);
        Assert.AreEqual(0f, tracker.RegisterHit(), Tolerance);
        Assert.AreEqual(2, tracker.Hits);
    }

    // A mistyped negative CastTime on an asset must not turn into a negative
    // delay (which would shorten the cast on every hit).
    [Test]
    public void NegativeCastTimeAddsNoDelay()
    {
        var tracker = new CastPushbackTracker(-1f);

        Assert.AreEqual(0f, tracker.RegisterHit(), Tolerance);
        Assert.AreEqual(1, tracker.Hits);
    }

    [Test]
    public void HitsCountEvenWhenTheyAddNoDelay()
    {
        var tracker = new CastPushbackTracker(2f);

        for (int i = 0; i < 5; i++) tracker.RegisterHit();

        Assert.AreEqual(5, tracker.Hits);
    }

    // The counter is per cast: a new cast is a new tracker, so it starts
    // back at the first (largest) fraction regardless of how many hits the
    // previous cast took.
    [Test]
    public void FreshTrackerStartsAtTheFirstFractionAgain()
    {
        var first = new CastPushbackTracker(3f);
        for (int i = 0; i < 4; i++) first.RegisterHit();

        var second = new CastPushbackTracker(3f);

        Assert.AreEqual(1.2f, second.RegisterHit(), Tolerance);
        Assert.AreEqual(1, second.Hits);
    }
}
