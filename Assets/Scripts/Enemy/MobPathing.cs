using UnityEngine;

// Picks which direction to move given an already-computed NavMesh path's
// corners (corners[0] is always the query's own source position, per
// NavMesh.CalculatePath's convention) - falls back to a straight line
// toward destination if the path is unusable (0 corners: no NavMesh baked
// yet, source/destination too far from any baked mesh - e.g. airborne
// mid-knockback - or genuinely unreachable). A PathPartial result (the
// destination can't be reached - e.g. the target is behind a carved
// Summon Wall) ends at the nearest reachable point: once the mob is
// within arriveDistance of that LAST corner this returns zero so the
// caller holds position there, instead of falling through to the
// straight line and pressing into whatever blocks it. arriveDistance is
// the distance the mob covers in one tick (RunSpeed x fixedDeltaTime,
// passed by the caller) - exactly one step's tolerance, so a mob that
// would overshoot the end stops on it rather than oscillating across it.
// Pure + testable with fake corner arrays - the actual
// NavMesh query can't be unit-tested (needs a real baked NavMesh in a
// scene).
//
// Takes a corner COUNT separately from the array, rather than relying on
// the array's own Length: callers read corners via
// NavMeshPath.GetCornersNonAlloc into a fixed-size reused buffer (the
// NavMeshPath.corners property allocates a new array on every single
// access - confirmed via Unity's own documented rationale for
// GetCornersNonAlloc existing at all - so reading it every tick, not just
// on recalc, would silently defeat the whole point of reusing one
// NavMeshPath instance), and only cornerCount of that buffer's slots hold
// this path's actual corners; the rest may be stale data from a longer
// previous path.
public static class MobPathing
{
    public static Vector3 DirectionTowardPath(Vector3 currentPosition, Vector3 destination, Vector3[] corners, int cornerCount, bool pathIsPartial = false, float arriveDistance = 0f)
    {
        if (pathIsPartial)
        {
            // The path's end is its last corner; with no corners at all the
            // mob's own position is as far as it gets.
            Vector3 end = corners != null && cornerCount > 0 ? corners[cornerCount - 1] : currentPosition;
            Vector3 toEnd = end - currentPosition;
            toEnd.y = 0f;
            if (toEnd.magnitude <= arriveDistance) return Vector3.zero;

            // A single corner is the query's snapped source (e.g. a mob
            // standing off-mesh at a carved hole's edge): there's nothing
            // beyond it to steer by, so walk to that snapped point - then
            // the arrival rule above holds it there - rather than fall
            // through to the straight line and press on past it.
            if (cornerCount == 1) return toEnd.normalized;
        }

        if (corners != null && cornerCount > 1)
        {
            Vector3 dir = corners[1] - currentPosition;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) return dir.normalized;
        }

        Vector3 straight = destination - currentPosition;
        straight.y = 0f;
        return straight.sqrMagnitude > 0.0001f ? straight.normalized : Vector3.zero;
    }
}
