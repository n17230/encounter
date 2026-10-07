using UnityEngine;

// Picks which direction to move given an already-computed NavMesh path's
// corners (corners[0] is always the query's own source position, per
// NavMesh.CalculatePath's convention) - falls back to a straight line
// toward destination if the path is unusable (0 corners: no NavMesh baked
// yet, source/destination too far from any baked mesh - e.g. airborne
// mid-knockback - or genuinely unreachable). A PathPartial result still
// produces a valid multi-corner path ending at the nearest reachable
// point, which this already walks toward correctly with no special
// handling needed. Pure + testable with fake corner arrays - the actual
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
    public static Vector3 DirectionTowardPath(Vector3 currentPosition, Vector3 destination, Vector3[] corners, int cornerCount)
    {
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
