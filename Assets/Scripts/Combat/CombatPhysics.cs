using UnityEngine;

// Shared physics queries for combat rules, so abilities and auto-attacks
// can't drift apart on what "line of sight" means.
public static class CombatPhysics
{
    // Chest-ish height both ends of a sight line are raised to, so the
    // ground itself doesn't block a line between two characters' feet.
    public const float SightHeight = 1.5f;

    private static readonly RaycastHit[] hitBuffer = new RaycastHit[32];

    // Only real environment geometry blocks sight. Creatures never do
    // (anything under a Targetable, whether it's the caster, the target, or
    // an unrelated mob standing in the way), and trigger volumes never do
    // (ground patches, following zones, pickups - none of them are walls).
    // Checks EVERY collider along the line rather than just the nearest: a
    // mob standing in front of a wall must not make the wall behind it
    // invisible.
    public static bool HasLineOfSight(Vector3 fromFeet, Vector3 toFeet)
    {
        Vector3 origin = fromFeet + Vector3.up * SightHeight;
        Vector3 delta = toFeet + Vector3.up * SightHeight - origin;
        float distance = delta.magnitude;
        if (distance < 0.001f) return true;

        int count = Physics.RaycastNonAlloc(origin, delta / distance, hitBuffer, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (hitBuffer[i].collider.GetComponentInParent<Targetable>() == null) return false;
        }
        return true;
    }
}
