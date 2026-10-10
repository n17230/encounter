using NUnit.Framework;
using UnityEngine;

public class MobPathingTests
{
    [Test]
    public void MultiCornerPathAimsAtSecondCorner()
    {
        Vector3 current = Vector3.zero;
        Vector3 destination = new Vector3(10f, 0f, 0f);
        Vector3[] corners = { current, new Vector3(0f, 0f, 5f), destination };

        Vector3 direction = MobPathing.DirectionTowardPath(current, destination, corners, corners.Length);

        Assert.AreEqual(Vector3.forward, direction);
    }

    [Test]
    public void NoCornersFallsBackToStraightLine()
    {
        Vector3 current = Vector3.zero;
        Vector3 destination = new Vector3(10f, 0f, 0f);

        Vector3 direction = MobPathing.DirectionTowardPath(current, destination, corners: null, cornerCount: 0);

        Assert.AreEqual(Vector3.right, direction);
    }

    [Test]
    public void SingleCornerFallsBackToStraightLine()
    {
        Vector3 current = Vector3.zero;
        Vector3[] corners = { current };

        Vector3 direction = MobPathing.DirectionTowardPath(current, new Vector3(0f, 0f, 10f), corners, corners.Length);

        Assert.AreEqual(Vector3.forward, direction);
    }

    [Test]
    public void ZeroDistanceDestinationReturnsZero()
    {
        Vector3 current = new Vector3(3f, 0f, 3f);

        Vector3 direction = MobPathing.DirectionTowardPath(current, current, corners: null, cornerCount: 0);

        Assert.AreEqual(Vector3.zero, direction);
    }

    // The query's own source position is always corners[0] - a corner
    // array that only contains it (no real waypoint beyond the start)
    // must fall back to the straight line, not divide by a zero vector.
    [Test]
    public void SecondCornerEqualToCurrentPositionFallsBackToStraightLine()
    {
        Vector3 current = Vector3.zero;
        Vector3[] corners = { current, current };

        Vector3 direction = MobPathing.DirectionTowardPath(current, new Vector3(0f, 0f, 10f), corners, corners.Length);

        Assert.AreEqual(Vector3.forward, direction);
    }

    // Only the horizontal component should steer movement - a corner
    // sitting above/below the mob (e.g. a ramp) must not tilt direction.
    [Test]
    public void VerticalOffsetIsIgnored()
    {
        Vector3 current = Vector3.zero;
        Vector3 destination = new Vector3(10f, 0f, 0f);
        Vector3[] corners = { current, new Vector3(5f, 3f, 0f), destination };

        Vector3 direction = MobPathing.DirectionTowardPath(current, destination, corners, corners.Length);

        Assert.AreEqual(Vector3.right, direction);
    }

    // A reused GetCornersNonAlloc buffer is typically larger than the
    // current path's actual corner count - only the first cornerCount
    // slots are this path's real data, the rest may be stale leftovers
    // from a previous, longer path. cornerCount (1, here) must govern
    // behavior, not the buffer array's own (larger) Length.
    [Test]
    public void CornerCountSmallerThanBufferIgnoresStaleTailData()
    {
        Vector3 current = Vector3.zero;
        Vector3 destination = new Vector3(0f, 0f, 10f);
        // Only index 0 (== current) is "real" for this call; indices 1-3
        // are stale data from a previous path that must be ignored.
        Vector3[] buffer = { current, new Vector3(99f, 0f, 99f), new Vector3(-50f, 0f, 2f), new Vector3(1f, 0f, 1f) };

        Vector3 direction = MobPathing.DirectionTowardPath(current, destination, buffer, cornerCount: 1);

        Assert.AreEqual(Vector3.forward, direction);
    }

    // --- Partial paths (destination unreachable, e.g. behind a carved
    // Summon Wall): hold at the last corner instead of pressing on. ---

    [Test]
    public void PartialPathAtLastCornerReturnsZero()
    {
        Vector3 current = Vector3.zero;
        Vector3 destination = new Vector3(0f, 0f, 0.05f);
        Vector3[] corners = { current, destination };

        Vector3 direction = MobPathing.DirectionTowardPath(current, destination, corners, corners.Length, pathIsPartial: true, arriveDistance: 0.1f);

        Assert.AreEqual(Vector3.zero, direction);
    }

    [Test]
    public void PartialPathNotYetAtLastCornerKeepsFollowingCorners()
    {
        Vector3 current = Vector3.zero;
        Vector3 destination = new Vector3(10f, 0f, 0f);
        Vector3[] corners = { current, new Vector3(0f, 0f, 5f), destination };

        Vector3 direction = MobPathing.DirectionTowardPath(current, destination, corners, corners.Length, pathIsPartial: true, arriveDistance: 0.1f);

        Assert.AreEqual(Vector3.forward, direction);
    }

    // Arrival is judged against the LAST corner - the second corner being
    // far away must not keep the mob walking once the path's end is here.
    [Test]
    public void PartialPathArrivalChecksTheLastCornerNotTheSecond()
    {
        Vector3 current = Vector3.zero;
        Vector3[] corners = { current, new Vector3(5f, 0f, 0f), new Vector3(0f, 0f, 0.05f) };

        Vector3 direction = MobPathing.DirectionTowardPath(current, new Vector3(10f, 0f, 0f), corners, corners.Length, pathIsPartial: true, arriveDistance: 0.1f);

        Assert.AreEqual(Vector3.zero, direction);
    }

    [Test]
    public void PartialPathArrivalIsHorizontalOnly()
    {
        Vector3 current = Vector3.zero;
        Vector3[] corners = { current, new Vector3(0f, 3f, 0.05f) };

        Vector3 direction = MobPathing.DirectionTowardPath(current, new Vector3(10f, 0f, 0f), corners, corners.Length, pathIsPartial: true, arriveDistance: 0.1f);

        Assert.AreEqual(Vector3.zero, direction);
    }

    // Pins "<=": exactly one step away counts as arrived, so the mob stops
    // on the end instead of overshooting it and turning back.
    [Test]
    public void PartialPathExactlyAtArriveDistanceReturnsZero()
    {
        Vector3 current = Vector3.zero;
        Vector3[] corners = { current, new Vector3(0f, 0f, 1f) };

        Vector3 direction = MobPathing.DirectionTowardPath(current, new Vector3(10f, 0f, 0f), corners, corners.Length, pathIsPartial: true, arriveDistance: 1f);

        Assert.AreEqual(Vector3.zero, direction);
    }

    [Test]
    public void PartialPathJustBeyondArriveDistanceKeepsMoving()
    {
        Vector3 current = Vector3.zero;
        Vector3[] corners = { current, new Vector3(0f, 0f, 1.5f) };

        Vector3 direction = MobPathing.DirectionTowardPath(current, new Vector3(10f, 0f, 0f), corners, corners.Length, pathIsPartial: true, arriveDistance: 1f);

        Assert.AreEqual(Vector3.forward, direction);
    }

    // No corners at all on a partial path: the mob's own position is the
    // end, so it's already there.
    [Test]
    public void PartialPathWithNoCornersReturnsZero()
    {
        Vector3 current = Vector3.zero;

        Vector3 direction = MobPathing.DirectionTowardPath(current, new Vector3(10f, 0f, 0f), corners: null, cornerCount: 0, pathIsPartial: true, arriveDistance: 0f);

        Assert.AreEqual(Vector3.zero, direction);
    }

    // Within arrive distance, but a complete path: the flag gates the
    // rule, so the mob keeps moving. 0.5 is exactly representable, so the
    // exact forward equality doesn't lean on float rounding.
    [Test]
    public void CompletePathAtLastCornerIsNotHeld()
    {
        Vector3 current = Vector3.zero;
        Vector3 destination = new Vector3(0f, 0f, 0.5f);
        Vector3[] corners = { current, destination };

        Vector3 direction = MobPathing.DirectionTowardPath(current, destination, corners, corners.Length, pathIsPartial: false, arriveDistance: 1f);

        Assert.AreEqual(Vector3.forward, direction);
    }

    // A single corner is the query's snapped source (an off-mesh mob at a
    // carved hole's edge): walk to that snapped point, not toward the
    // unreachable destination.
    [Test]
    public void PartialPathSingleCornerBeyondArriveDistanceWalksToThatCorner()
    {
        Vector3 current = Vector3.zero;
        Vector3[] corners = { new Vector3(0f, 0f, 0.5f) };

        Vector3 direction = MobPathing.DirectionTowardPath(current, new Vector3(10f, 0f, 0f), corners, cornerCount: 1, pathIsPartial: true, arriveDistance: 0.1f);

        Assert.AreEqual(Vector3.forward, direction);
    }

    // The arrival compare is linear, not squared: 0.7 > 0.5 must move
    // (0.49 <= 0.5 would wrongly hold) ...
    [Test]
    public void PartialPathArrivalIsLinearNotSquaredBelow()
    {
        Vector3 current = Vector3.zero;
        Vector3[] corners = { current, new Vector3(0f, 0f, 0.7f) };

        Vector3 direction = MobPathing.DirectionTowardPath(current, new Vector3(10f, 0f, 0f), corners, corners.Length, pathIsPartial: true, arriveDistance: 0.5f);

        Assert.AreEqual(Vector3.forward, direction);
    }

    // ... and 1.5 <= 2 must hold (2.25 > 2 would wrongly move).
    [Test]
    public void PartialPathArrivalIsLinearNotSquaredAbove()
    {
        Vector3 current = Vector3.zero;
        Vector3[] corners = { current, new Vector3(0f, 0f, 1.5f) };

        Vector3 direction = MobPathing.DirectionTowardPath(current, new Vector3(10f, 0f, 0f), corners, corners.Length, pathIsPartial: true, arriveDistance: 2f);

        Assert.AreEqual(Vector3.zero, direction);
    }

    // Models the real EnemyAI call: the reused buffer is never null, and a
    // zero corner count with stale data in slot 0 must still read as "no
    // corners" (the mob's own position is the end).
    [Test]
    public void PartialPathWithNonNullBufferAndZeroCountReturnsZero()
    {
        Vector3 current = Vector3.zero;
        Vector3[] buffer = { new Vector3(99f, 0f, 99f) };

        Vector3 direction = MobPathing.DirectionTowardPath(current, new Vector3(10f, 0f, 0f), buffer, cornerCount: 0, pathIsPartial: true, arriveDistance: 0.1f);

        Assert.AreEqual(Vector3.zero, direction);
    }

    // Single corner equal to the mob's own position with zero tolerance:
    // already there.
    [Test]
    public void PartialPathSingleCornerAtCurrentPositionReturnsZero()
    {
        Vector3 current = Vector3.zero;
        Vector3[] corners = { current };

        Vector3 direction = MobPathing.DirectionTowardPath(current, new Vector3(10f, 0f, 0f), corners, cornerCount: 1, pathIsPartial: true, arriveDistance: 0f);

        Assert.AreEqual(Vector3.zero, direction);
    }

    // Not yet arrived, and a degenerate corners[1] (equal to current):
    // the existing straight-line fallback still applies on a partial
    // path - only the single-corner case is special.
    [Test]
    public void PartialPathDegenerateSecondCornerFallsBackToStraightLine()
    {
        Vector3 current = Vector3.zero;
        Vector3 destination = new Vector3(10f, 0f, 0f);
        Vector3[] corners = { current, current, destination };

        Vector3 direction = MobPathing.DirectionTowardPath(current, destination, corners, cornerCount: 3, pathIsPartial: true, arriveDistance: 0.1f);

        Assert.AreEqual(Vector3.right, direction);
    }

    // The last corner is index cornerCount-1, not the buffer's last slot -
    // stale tail data from a longer previous path must be ignored here too.
    [Test]
    public void PartialPathAtEndIgnoresStaleTailData()
    {
        Vector3 current = Vector3.zero;
        Vector3[] buffer = { current, new Vector3(0f, 0f, 0.05f), new Vector3(99f, 0f, 99f) };

        Vector3 direction = MobPathing.DirectionTowardPath(current, new Vector3(10f, 0f, 0f), buffer, cornerCount: 2, pathIsPartial: true, arriveDistance: 0.1f);

        Assert.AreEqual(Vector3.zero, direction);
    }
}
