using System.Collections.Generic;

// Proximity: ignore threat entirely, always chase the nearest player -
//   goblins use this (deliberately threat-blind).
// HighestThreat: standard aggro - most damage dealt, tie-break nearest.
// LowestThreat: inverse aggro - whoever has contributed LEAST (an
//   untouched player counts as 0), tie-break nearest.
// FarthestPlayer: whichever player is farthest away, ignoring threat.
public enum TargetingMode
{
    Proximity,
    HighestThreat,
    LowestThreat,
    FarthestPlayer
}

public struct TargetCandidate<T>
{
    public T Subject;
    public float Threat;
    public float Distance;
}

// Pure target-selection rules for EnemyAI, kept free of scene/network
// types so they can be unit-tested.
public static class TargetSelector
{
    // Threat-based modes fall back to nearest when the mob has no threat
    // table at all.
    public static T Select<T>(IReadOnlyList<TargetCandidate<T>> candidates, TargetingMode mode, bool hasThreatTable) where T : class
    {
        if (candidates == null || candidates.Count == 0) return null;

        switch (mode)
        {
            case TargetingMode.HighestThreat:
                return hasThreatTable ? ByThreat(candidates, highest: true) : ByDistance(candidates, nearest: true);
            case TargetingMode.LowestThreat:
                return hasThreatTable ? ByThreat(candidates, highest: false) : ByDistance(candidates, nearest: true);
            case TargetingMode.FarthestPlayer:
                return ByDistance(candidates, nearest: false);
            default:
                return ByDistance(candidates, nearest: true);
        }
    }

    private static T ByThreat<T>(IReadOnlyList<TargetCandidate<T>> candidates, bool highest)
    {
        TargetCandidate<T> best = candidates[0];
        for (int i = 1; i < candidates.Count; i++)
        {
            TargetCandidate<T> candidate = candidates[i];
            bool better = highest ? candidate.Threat > best.Threat : candidate.Threat < best.Threat;
            bool tieButCloser = candidate.Threat == best.Threat && candidate.Distance < best.Distance;
            if (better || tieButCloser) best = candidate;
        }
        return best.Subject;
    }

    private static T ByDistance<T>(IReadOnlyList<TargetCandidate<T>> candidates, bool nearest)
    {
        TargetCandidate<T> best = candidates[0];
        for (int i = 1; i < candidates.Count; i++)
        {
            TargetCandidate<T> candidate = candidates[i];
            bool better = nearest ? candidate.Distance < best.Distance : candidate.Distance > best.Distance;
            if (better) best = candidate;
        }
        return best.Subject;
    }
}
