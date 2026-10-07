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
}
