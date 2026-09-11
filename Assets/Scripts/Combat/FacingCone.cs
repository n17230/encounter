using UnityEngine;

// Yaw-only "is the target in front of me" test shared by casting and
// melee. Pitch/camera angle deliberately doesn't matter.
public static class FacingCone
{
    public static bool IsWithin(Transform self, Vector3 targetPosition, float coneAngle)
    {
        Vector3 toTarget = targetPosition - self.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) return true;

        Vector3 facing = self.forward;
        facing.y = 0f;
        facing.Normalize();

        return Vector3.Angle(facing, toTarget.normalized) <= coneAngle * 0.5f;
    }
}
