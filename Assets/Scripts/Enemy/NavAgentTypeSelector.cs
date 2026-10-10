using System.Collections.Generic;

// One baked NavMesh agent type's footprint (UnityEngine.AI.NavMeshBuildSettings
// agentTypeID / agentRadius / agentHeight), as plain data so the selection
// below stays pure and testable.
public struct NavAgentTypeSize
{
    public int Id;
    public float Radius;
    public float Height;
}

// Picks which NavMesh agent type a mob should path on from its own capsule
// size: the smallest type whose radius AND height still contain it (a path
// planned for a smaller agent routes it through gaps it can't physically
// fit through), or the largest type available if the mob is bigger than
// all of them, or fallbackId if there are none. Data-driven on purpose -
// no per-prefab setting to forget when a new big mob is added.
public static class NavAgentTypeSelector
{
    private const float Tolerance = 0.01f;

    public static int Select(float radius, float height, IReadOnlyList<NavAgentTypeSize> types, int fallbackId)
    {
        bool haveFit = false;
        NavAgentTypeSize bestFit = default;
        bool haveLargest = false;
        NavAgentTypeSize largest = default;

        for (int i = 0; i < types.Count; i++)
        {
            NavAgentTypeSize candidate = types[i];

            if (!haveLargest || IsLarger(candidate, largest))
            {
                largest = candidate;
                haveLargest = true;
            }

            bool fits = candidate.Radius >= radius - Tolerance && candidate.Height >= height - Tolerance;
            if (fits && (!haveFit || IsLarger(bestFit, candidate)))
            {
                bestFit = candidate;
                haveFit = true;
            }
        }

        if (haveFit) return bestFit.Id;
        if (haveLargest) return largest.Id;
        return fallbackId;
    }

    // Radius first, then height.
    private static bool IsLarger(NavAgentTypeSize a, NavAgentTypeSize b)
    {
        if (a.Radius != b.Radius) return a.Radius > b.Radius;
        return a.Height > b.Height;
    }
}
