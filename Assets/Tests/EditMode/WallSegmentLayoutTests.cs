using NUnit.Framework;
using UnityEngine;

public class WallSegmentLayoutTests
{
    private const float Tolerance = 1e-5f;

    private static float[] Flat(int count, float y)
    {
        float[] heights = new float[count];
        for (int i = 0; i < count; i++) heights[i] = y;
        return heights;
    }

    [Test]
    public void TwentySegmentsStraddleTheAimedPoint()
    {
        Assert.AreEqual(-9.5f, WallSegmentLayout.Offset(0, 20, 1f), Tolerance);
        Assert.AreEqual(-0.5f, WallSegmentLayout.Offset(9, 20, 1f), Tolerance);
        Assert.AreEqual(0.5f, WallSegmentLayout.Offset(10, 20, 1f), Tolerance);
        Assert.AreEqual(9.5f, WallSegmentLayout.Offset(19, 20, 1f), Tolerance);

        foreach (float offset in WallSegmentLayout.Offsets(20, 1f))
        {
            Assert.Greater(Mathf.Abs(offset), Tolerance);
        }
    }

    [Test]
    public void OddCountPutsOneSegmentOnTheAimedPoint()
    {
        float[] five = WallSegmentLayout.Offsets(5, 1f);
        Assert.AreEqual(5, five.Length);
        for (int i = 0; i < 5; i++) Assert.AreEqual(i - 2, five[i], Tolerance);

        Assert.AreEqual(0f, WallSegmentLayout.Offsets(1, 1f)[0], Tolerance);
    }

    [Test]
    public void OffsetsScaleWithSegmentWidth()
    {
        float[] offsets = WallSegmentLayout.Offsets(4, 2f);
        Assert.AreEqual(-3f, offsets[0], Tolerance);
        Assert.AreEqual(-1f, offsets[1], Tolerance);
        Assert.AreEqual(1f, offsets[2], Tolerance);
        Assert.AreEqual(3f, offsets[3], Tolerance);
    }

    [Test]
    public void SegmentWorldXZMovesAlongTheRightAxis()
    {
        Vector2 alongX = WallSegmentLayout.SegmentWorldXZ(new Vector2(10f, 20f), new Vector3(1f, 0f, 0f), 2.5f);
        Assert.AreEqual(12.5f, alongX.x, Tolerance);
        Assert.AreEqual(20f, alongX.y, Tolerance);

        Vector2 alongZ = WallSegmentLayout.SegmentWorldXZ(new Vector2(10f, 20f), new Vector3(0f, 0f, 1f), -9.5f);
        Assert.AreEqual(10f, alongZ.x, Tolerance);
        Assert.AreEqual(10.5f, alongZ.y, Tolerance);
    }

    [Test]
    public void FlatGroundSkipsNothing()
    {
        Assert.AreEqual(0u, WallSegmentLayout.ComputeSkipMask(Flat(20, 10f), 10f, 4f));
    }

    [Test]
    public void MaskKeepsExactlyTheLimitAndSkipsBeyondIt()
    {
        float[] heights = { 14.0f, 14.01f, 5.99f, 6.0f };
        uint mask = WallSegmentLayout.ComputeSkipMask(heights, 10f, 4f);

        Assert.IsFalse(WallSegmentLayout.IsSkipped(mask, 0));
        Assert.IsTrue(WallSegmentLayout.IsSkipped(mask, 1));
        Assert.IsTrue(WallSegmentLayout.IsSkipped(mask, 2));
        Assert.IsFalse(WallSegmentLayout.IsSkipped(mask, 3));
    }

    [Test]
    public void SingleCliffSkipsOnlyThatSegment()
    {
        float[] heights = Flat(20, 10f);
        heights[7] = 30f;

        Assert.AreEqual(1u << 7, WallSegmentLayout.ComputeSkipMask(heights, 10f, 4f));
    }

    [Test]
    public void AllSegmentsCanBeSkipped()
    {
        Assert.AreEqual(0xFFFFFu, WallSegmentLayout.ComputeSkipMask(Flat(20, 100f), 0f, 4f));
    }

    [Test]
    public void MaskIsRelativeToTheAnchorNotTheFirstOrMiddleSegment()
    {
        float[] heights = Flat(20, 10f);
        heights[0] = 15f;
        heights[10] = 15f;

        uint mask = WallSegmentLayout.ComputeSkipMask(heights, 10f, 4f);

        Assert.AreEqual((1u << 0) | (1u << 10), mask);
    }

    [Test]
    public void IsSkippedMatchesEachBitIncludingTheEnds()
    {
        uint mask = (1u << 0) | (1u << 31);

        Assert.IsTrue(WallSegmentLayout.IsSkipped(mask, 0));
        Assert.IsFalse(WallSegmentLayout.IsSkipped(mask, 1));
        Assert.IsFalse(WallSegmentLayout.IsSkipped(mask, 30));
        Assert.IsTrue(WallSegmentLayout.IsSkipped(mask, 31));
    }

    [Test]
    public void ZeroOrNegativeCountIsEmpty()
    {
        Assert.AreEqual(0u, WallSegmentLayout.ComputeSkipMask(new float[0], 0f, 4f));
        Assert.AreEqual(0u, WallSegmentLayout.ComputeSkipMask(null, 0f, 4f));
        Assert.AreEqual(0, WallSegmentLayout.Offsets(0, 1f).Length);
        Assert.AreEqual(0, WallSegmentLayout.Offsets(-3, 1f).Length);
    }

    [Test]
    public void CountsPastThirtyTwoClamp()
    {
        Assert.AreEqual(32, WallSegmentLayout.ClampCount(40));
        Assert.AreEqual(32, WallSegmentLayout.Offsets(40, 1f).Length);
        // Segments past the 32nd can't be represented in the mask and are ignored.
        Assert.AreEqual(0u, WallSegmentLayout.ComputeSkipMask(Flat(40, 100f), 100f, 4f));
        Assert.AreEqual(0xFFFFFFFFu, WallSegmentLayout.ComputeSkipMask(Flat(40, 100f), 0f, 4f));
    }

    [Test]
    public void IsSkippedIgnoresOutOfRangeIndices()
    {
        uint all = 0xFFFFFFFFu;

        Assert.IsFalse(WallSegmentLayout.IsSkipped(all, 32));
        Assert.IsFalse(WallSegmentLayout.IsSkipped(all, 100));
        Assert.IsFalse(WallSegmentLayout.IsSkipped(all, -1));
        Assert.IsFalse(WallSegmentLayout.IsSkipped(all, int.MaxValue));
    }

    [Test]
    public void MaskIgnoresHeightsPastThirtyTwoEvenWhenFar()
    {
        float[] heights = Flat(40, 10f);
        for (int i = 32; i < 40; i++) heights[i] = 500f;

        Assert.AreEqual(0u, WallSegmentLayout.ComputeSkipMask(heights, 10f, 4f));
    }

    [Test]
    public void OffsetsAtTheClampStayCentred()
    {
        float[] offsets = WallSegmentLayout.Offsets(40, 1f);

        Assert.AreEqual(-15.5f, offsets[0], Tolerance);
        Assert.AreEqual(15.5f, offsets[31], Tolerance);
    }

    [Test]
    public void ClampCountPinsItsBounds()
    {
        Assert.AreEqual(32, WallSegmentLayout.ClampCount(32));
        Assert.AreEqual(0, WallSegmentLayout.ClampCount(0));
        Assert.AreEqual(1, WallSegmentLayout.ClampCount(1));
    }

    [Test]
    public void SegmentWorldXZUsesRightXAndZOnly()
    {
        Vector2 result = WallSegmentLayout.SegmentWorldXZ(new Vector2(10f, 20f), new Vector3(0.6f, 0.7f, 0.8f), 5f);

        Assert.AreEqual(13f, result.x, Tolerance);
        Assert.AreEqual(24f, result.y, Tolerance);
    }

    [Test]
    public void CliffAtEitherEndSetsOnlyThatBit()
    {
        float[] last = Flat(20, 10f);
        last[19] = 50f;
        Assert.AreEqual(1u << 19, WallSegmentLayout.ComputeSkipMask(last, 10f, 4f));

        float[] first = Flat(20, 10f);
        first[0] = 50f;
        Assert.AreEqual(1u, WallSegmentLayout.ComputeSkipMask(first, 10f, 4f));
    }

    [Test]
    public void BoxHeightIncludesTheBelowGroundDepth()
    {
        Assert.AreEqual(6f, WallSegmentLayout.BoxHeight(5f, 1f), Tolerance);
    }

    [Test]
    public void BoxCentreSitsBetweenBelowGroundBottomAndTop()
    {
        // Bottom at 9, top at 15.
        Assert.AreEqual(12f, WallSegmentLayout.BoxCenterY(10f, 5f, 1f), Tolerance);
        Assert.AreEqual(12.5f, WallSegmentLayout.BoxCenterY(10f, 5f, 0f), Tolerance);
    }

    [Test]
    public void SlopeSkipsOnlyPastTheLimitFromTheAnchor()
    {
        float[] heights = new float[20];
        for (int i = 0; i < 20; i++) heights[i] = 0.5f * i;

        uint mask = WallSegmentLayout.ComputeSkipMask(heights, 5f, 4f);

        // i = 2 and 18 sit at exactly 4.0 from the anchor and are kept.
        Assert.AreEqual((1u << 0) | (1u << 1) | (1u << 19), mask);
    }

    [Test]
    public void ThirtySegmentsRunFromMinusToPlusFourteenAndAHalf()
    {
        float[] offsets = WallSegmentLayout.Offsets(30, 1f);

        Assert.AreEqual(30, offsets.Length);
        Assert.AreEqual(-14.5f, offsets[0], Tolerance);
        Assert.AreEqual(14.5f, offsets[29], Tolerance);
    }

    [Test]
    public void OnlyTheLastOfThirtyCliffedSetsBit29()
    {
        float[] heights = Flat(30, 10f);
        heights[29] = 50f;

        uint mask = WallSegmentLayout.ComputeSkipMask(heights, 10f, 4f);

        Assert.AreEqual(1u << 29, mask);
        Assert.IsTrue(WallSegmentLayout.IsSkipped(mask, 29));
        Assert.IsFalse(WallSegmentLayout.IsSkipped(mask, 28));
    }

    [TestCase(30f, 1f, 30)]
    [TestCase(20f, 1f, 20)]
    [TestCase(100f, 1f, 32)]
    [TestCase(0f, 1f, 1)]
    [TestCase(0.4f, 1f, 1)]
    [TestCase(-5f, 1f, 1)]
    [TestCase(30f, 2f, 15)]
    public void SegmentCountRoundsAndClamps(float totalWidth, float segmentWidth, int expected)
    {
        Assert.AreEqual(expected, WallSegmentLayout.SegmentCount(totalWidth, segmentWidth));
    }

    [TestCase(30f, 1f, 30f)]
    [TestCase(100f, 1f, 32f)]
    [TestCase(20.4f, 1f, 20f)]
    public void EffectiveWidthIsWholeSegments(float totalWidth, float segmentWidth, float expected)
    {
        Assert.AreEqual(expected, WallSegmentLayout.EffectiveWidth(totalWidth, segmentWidth), Tolerance);
    }

    private static void AssertFacing(Quaternion rotation, Vector3 expectedForward, Vector3 expectedRight)
    {
        Vector3 forward = rotation * Vector3.forward;
        Vector3 right = rotation * Vector3.right;
        Assert.AreEqual(expectedForward.x, forward.x, 1e-4f);
        Assert.AreEqual(expectedForward.y, forward.y, 1e-4f);
        Assert.AreEqual(expectedForward.z, forward.z, 1e-4f);
        Assert.AreEqual(expectedRight.x, right.x, 1e-4f);
        Assert.AreEqual(expectedRight.y, right.y, 1e-4f);
        Assert.AreEqual(expectedRight.z, right.z, 1e-4f);
    }

    [Test]
    public void FacingRotationUsesTheRequestedDirection()
    {
        AssertFacing(WallSegmentLayout.FacingRotation(new Vector2(0f, 1f), Vector3.right), new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f));
        AssertFacing(WallSegmentLayout.FacingRotation(new Vector2(1f, 0f), Vector3.forward), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, -1f));
        // Length doesn't matter, only direction.
        AssertFacing(WallSegmentLayout.FacingRotation(new Vector2(0f, 5f), Vector3.right), new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f));
    }

    [Test]
    public void FacingRotationFallsBackToTheFlattenedForward()
    {
        AssertFacing(WallSegmentLayout.FacingRotation(Vector2.zero, new Vector3(1f, 0f, 0f)), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, -1f));
        // The fallback's Y is dropped.
        AssertFacing(WallSegmentLayout.FacingRotation(Vector2.zero, new Vector3(0f, 1f, 1f)), new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f));
    }

    [Test]
    public void FacingRotationEndsAtWorldForward()
    {
        AssertFacing(WallSegmentLayout.FacingRotation(Vector2.zero, Vector3.zero), new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f));
        AssertFacing(WallSegmentLayout.FacingRotation(Vector2.zero, new Vector3(0f, 5f, 0f)), new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f));
    }

    [Test]
    public void FacingRotationRejectsNonFiniteAndTinyRequests()
    {
        Vector3 fallback = new Vector3(1f, 0f, 0f);
        Vector3 expectedForward = new Vector3(1f, 0f, 0f);
        Vector3 expectedRight = new Vector3(0f, 0f, -1f);

        AssertFacing(WallSegmentLayout.FacingRotation(new Vector2(float.NaN, 1f), fallback), expectedForward, expectedRight);
        AssertFacing(WallSegmentLayout.FacingRotation(new Vector2(float.PositiveInfinity, 0f), fallback), expectedForward, expectedRight);
        AssertFacing(WallSegmentLayout.FacingRotation(new Vector2(1e30f, 0f), fallback), expectedForward, expectedRight);
        AssertFacing(WallSegmentLayout.FacingRotation(new Vector2(0.001f, 0f), fallback), expectedForward, expectedRight);
    }

    // Rounds to nearest (not floor): .6 fractions go up.
    [TestCase(20.6f, 1f, 21)]
    [TestCase(0.6f, 1f, 1)]
    [TestCase(1.6f, 1f, 2)]
    [TestCase(30f, 0f, 1)]
    [TestCase(30f, -1f, 1)]
    [TestCase(float.NaN, 1f, 1)]
    [TestCase(float.PositiveInfinity, 1f, 1)]
    public void SegmentCountRoundsToNearestAndSurvivesBadInput(float totalWidth, float segmentWidth, int expected)
    {
        Assert.AreEqual(expected, WallSegmentLayout.SegmentCount(totalWidth, segmentWidth));
    }

    [TestCase(30f, 2f, 30f)]
    [TestCase(7f, 2f, 8f)]
    public void EffectiveWidthScalesWithSegmentWidth(float totalWidth, float segmentWidth, float expected)
    {
        Assert.AreEqual(expected, WallSegmentLayout.EffectiveWidth(totalWidth, segmentWidth), Tolerance);
    }

    [Test]
    public void FacingRotationEpsilonAndFallbackDirections()
    {
        // Just under the length threshold: fallback.
        AssertFacing(WallSegmentLayout.FacingRotation(new Vector2(0.001f, 0f), Vector3.forward), new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f));
        // Squared length 4e-4, over the threshold: the request is used.
        AssertFacing(WallSegmentLayout.FacingRotation(new Vector2(0.02f, 0f), Vector3.forward), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, -1f));
        // Fallback directions are taken as given.
        AssertFacing(WallSegmentLayout.FacingRotation(Vector2.zero, new Vector3(0f, 0f, -1f)), new Vector3(0f, 0f, -1f), new Vector3(-1f, 0f, 0f));

        float diagonal = 0.70710678f;
        Quaternion rotation = WallSegmentLayout.FacingRotation(Vector2.zero, new Vector3(1f, 0f, 1f));
        Vector3 forward = rotation * Vector3.forward;
        Assert.AreEqual(diagonal, forward.x, 1e-4f);
        Assert.AreEqual(0f, forward.y, 1e-4f);
        Assert.AreEqual(diagonal, forward.z, 1e-4f);
    }

    [Test]
    public void FacingRotationNormalisesANonUnitRequest()
    {
        Vector3 forward = WallSegmentLayout.FacingRotation(new Vector2(-3f, 0f), Vector3.forward) * Vector3.forward;

        Assert.AreEqual(-1f, forward.x, 1e-4f);
        Assert.AreEqual(0f, forward.z, 1e-4f);
        Assert.AreEqual(1f, forward.magnitude, 1e-4f);
    }

    [Test]
    public void StripCornersOnDiagonalAxesAtAnOffset()
    {
        Vector3[] dest = new Vector3[8];

        WallSegmentLayout.WriteStripCorners(new Vector2(10f, 20f), new Vector3(0.6f, 0f, 0.8f), new Vector3(-0.8f, 0f, 0.6f), 3f, 1f, 2f, dest, 4);

        float[,] expected = { { 9.5f, 18.5f }, { 11.3f, 20.9f }, { 10.5f, 21.5f }, { 8.7f, 19.1f } };
        for (int i = 0; i < 4; i++)
        {
            Assert.AreEqual(expected[i, 0], dest[4 + i].x, 1e-4f);
            Assert.AreEqual(2f, dest[4 + i].y, 1e-4f);
            Assert.AreEqual(expected[i, 1], dest[4 + i].z, 1e-4f);
            Assert.AreEqual(Vector3.zero, dest[i]);
        }
    }

    [Test]
    public void StripCornersGoAroundTheSegmentInOrder()
    {
        Vector3[] dest = new Vector3[4];

        WallSegmentLayout.WriteStripCorners(new Vector2(10f, 20f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f), 1f, 2f, 5.05f, dest, 0);

        Assert.AreEqual(new Vector3(9.5f, 5.05f, 19f), dest[0]);
        Assert.AreEqual(new Vector3(10.5f, 5.05f, 19f), dest[1]);
        Assert.AreEqual(new Vector3(10.5f, 5.05f, 21f), dest[2]);
        Assert.AreEqual(new Vector3(9.5f, 5.05f, 21f), dest[3]);
    }

    [Test]
    public void StripCornersFollowRotatedAxes()
    {
        Vector3[] dest = new Vector3[4];

        WallSegmentLayout.WriteStripCorners(new Vector2(10f, 20f), new Vector3(0f, 0f, -1f), new Vector3(1f, 0f, 0f), 1f, 2f, 5.05f, dest, 0);

        Assert.AreEqual(9f, dest[0].x, Tolerance);
        Assert.AreEqual(20.5f, dest[0].z, Tolerance);
        Assert.AreEqual(9f, dest[1].x, Tolerance);
        Assert.AreEqual(19.5f, dest[1].z, Tolerance);
        Assert.AreEqual(11f, dest[2].x, Tolerance);
        Assert.AreEqual(19.5f, dest[2].z, Tolerance);
        Assert.AreEqual(11f, dest[3].x, Tolerance);
        Assert.AreEqual(20.5f, dest[3].z, Tolerance);
    }

    [Test]
    public void StripCornersWriteOnlyTheirOwnRangeAndIgnoreAxisY()
    {
        Vector3[] dest = new Vector3[12];

        WallSegmentLayout.WriteStripCorners(new Vector2(10f, 20f), new Vector3(1f, 9f, 0f), new Vector3(0f, 9f, 1f), 1f, 2f, 5.05f, dest, 4);

        for (int i = 0; i < 4; i++) Assert.AreEqual(Vector3.zero, dest[i]);
        for (int i = 8; i < 12; i++) Assert.AreEqual(Vector3.zero, dest[i]);
        for (int i = 4; i < 8; i++) Assert.AreEqual(5.05f, dest[i].y, Tolerance);
        Assert.AreEqual(new Vector3(9.5f, 5.05f, 19f), dest[4]);
    }
}
