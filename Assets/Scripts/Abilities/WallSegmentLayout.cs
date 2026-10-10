using UnityEngine;

// Pure layout math for SegmentedWall (Summon Wall): where each segment sits
// along the wall line, which segments a cliff removes, and the box
// dimensions of one segment. No scene access, so it's unit-testable
// (Assets/Tests/EditMode/WallSegmentLayoutTests.cs).
public static class WallSegmentLayout
{
    // The skip mask is one bit per segment in a uint.
    public const int MaxSegments = 32;

    public static int ClampCount(int count)
    {
        return Mathf.Clamp(count, 0, MaxSegments);
    }

    // Distance of segment `index` from the aimed point along the wall's
    // width axis, centred so an even count straddles the point (no segment
    // sits on it) and an odd count puts one segment exactly on it.
    public static float Offset(int index, int count, float segmentWidth)
    {
        return (index - (count - 1) * 0.5f) * segmentWidth;
    }

    // Every segment's offset, in index order. Empty for count <= 0.
    public static float[] Offsets(int count, float segmentWidth)
    {
        int clamped = ClampCount(count);
        float[] offsets = new float[clamped];
        for (int i = 0; i < clamped; i++) offsets[i] = Offset(i, clamped, segmentWidth);
        return offsets;
    }

    // Horizontal position of a segment: the aimed point's X/Z moved `offset`
    // along the wall's right axis (only right's X/Z are used).
    public static Vector2 SegmentWorldXZ(Vector2 centreXZ, Vector3 right, float offset)
    {
        return new Vector2(centreXZ.x + right.x * offset, centreXZ.y + right.z * offset);
    }

    // Bit i set = segment i is skipped: its ground differs from the anchor
    // by MORE than maxDelta (exactly maxDelta is kept). Compared against the
    // anchor, never against a neighbour, so a long gentle slope that drifts
    // past maxDelta cuts off at the same segments from either end. Segments
    // past MaxSegments are ignored.
    public static uint ComputeSkipMask(float[] heights, float anchorY, float maxDelta)
    {
        if (heights == null) return 0;

        uint mask = 0;
        int count = ClampCount(heights.Length);
        for (int i = 0; i < count; i++)
        {
            if (Mathf.Abs(heights[i] - anchorY) > maxDelta) mask |= 1u << i;
        }
        return mask;
    }

    public static bool IsSkipped(uint mask, int index)
    {
        if (index < 0 || index >= MaxSegments) return false;
        return (mask & (1u << index)) != 0;
    }

    // How many segments a wall of totalWidth is split into: at least 1, at
    // most MaxSegments. Shared by the server's spawn, the aim preview and
    // the tooltip so they can never disagree on it.
    public static int SegmentCount(float totalWidth, float segmentWidth)
    {
        if (!(segmentWidth > 0f)) return 1;
        float ratio = totalWidth / segmentWidth;
        if (float.IsNaN(ratio) || float.IsInfinity(ratio)) return 1;
        return ClampCount(Mathf.Max(1, Mathf.RoundToInt(ratio)));
    }

    // The width the wall actually ends up: the clamped count of whole
    // segments, not the authored StructureWidth.
    public static float EffectiveWidth(float totalWidth, float segmentWidth)
    {
        return SegmentCount(totalWidth, segmentWidth) * segmentWidth;
    }

    // Flat orientation for a wall. Prefers the owner's facing sent with the
    // cast; falls back to the caster's replicated forward, then world
    // forward. Never rejects - a cast with a bad facing still places a wall.
    // The same call builds the aim preview and the server's wall, so what
    // you saw is what you get.
    public static Quaternion FacingRotation(Vector2 requestedFlat, Vector3 fallbackForward)
    {
        if (TryNormalize(requestedFlat.x, requestedFlat.y, out Vector3 direction) ||
            TryNormalize(fallbackForward.x, fallbackForward.z, out direction))
        {
            return Quaternion.LookRotation(direction, Vector3.up);
        }
        return Quaternion.LookRotation(Vector3.forward, Vector3.up);
    }

    private static bool TryNormalize(float x, float z, out Vector3 direction)
    {
        direction = Vector3.forward;
        float sqr = x * x + z * z;
        // Non-finite or too-short input can't give a direction. (1e30 squared
        // overflows to infinity, so it's rejected here too.)
        if (float.IsNaN(sqr) || float.IsInfinity(sqr) || sqr <= 0.0001f) return false;

        float inv = 1f / Mathf.Sqrt(sqr);
        direction = new Vector3(x * inv, 0f, z * inv);
        return true;
    }

    // Writes one flat quad's four corners (-r,-f), (+r,-f), (+r,+f), (-r,+f)
    // around segmentXZ into dest[destIndex..destIndex+3], all at height y.
    // Only the axes' X/Z are used.
    public static void WriteStripCorners(Vector2 segmentXZ, Vector3 right, Vector3 forward, float width, float thickness, float y, Vector3[] dest, int destIndex)
    {
        float hw = width * 0.5f;
        float ht = thickness * 0.5f;
        float rx = right.x * hw, rz = right.z * hw;
        float fx = forward.x * ht, fz = forward.z * ht;

        dest[destIndex] = new Vector3(segmentXZ.x - rx - fx, y, segmentXZ.y - rz - fz);
        dest[destIndex + 1] = new Vector3(segmentXZ.x + rx - fx, y, segmentXZ.y + rz - fz);
        dest[destIndex + 2] = new Vector3(segmentXZ.x + rx + fx, y, segmentXZ.y + rz + fz);
        dest[destIndex + 3] = new Vector3(segmentXZ.x - rx + fx, y, segmentXZ.y - rz + fz);
    }

    // A segment's box reaches `depth` below its ground as well as `height`
    // above, so a step between neighbouring segments leaves no gap under it.
    public static float BoxHeight(float height, float depth)
    {
        return height + depth;
    }

    // World Y of the box's centre: bottom at groundY - depth, top at
    // groundY + height.
    public static float BoxCenterY(float groundY, float height, float depth)
    {
        return groundY - depth + BoxHeight(height, depth) * 0.5f;
    }
}
