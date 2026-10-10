using System.Collections.Generic;

// Proximity: ignore threat entirely, always chase the nearest player -
//   goblins use this (deliberately threat-blind).
// HighestThreat: standard aggro - most damage dealt, tie-break nearest.
// LowestThreat: inverse aggro - whoever has contributed LEAST (an
//   untouched player counts as 0), tie-break nearest.
// FarthestPlayer: whichever player is farthest away, ignoring threat.
// MostHealing: whoever has the most healing credit on this mob's table
//   (ThreatTable.AddHealing), tie-break nearest; with no credit yet it is
//   exactly HighestThreat, so an unhealed pull still behaves normally.
// New values are always appended - the int is serialized on mob prefabs.
public enum TargetingMode
{
    Proximity,
    HighestThreat,
    LowestThreat,
    FarthestPlayer,
    MostHealing
}

public struct TargetCandidate<T>
{
    public T Subject;
    public float Threat;
    public float Distance;
    // Healing credit on the mob's table (0 for every mode but MostHealing).
    public float Healing;
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
            case TargetingMode.MostHealing:
                if (!hasThreatTable) return ByDistance(candidates, nearest: true);
                return AnyHealing(candidates) ? ByHealing(candidates) : ByThreat(candidates, highest: true);
            default:
                return ByDistance(candidates, nearest: true);
        }
    }

    private static bool AnyHealing<T>(IReadOnlyList<TargetCandidate<T>> candidates)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].Healing > 0f) return true;
        }
        return false;
    }

    private static T ByHealing<T>(IReadOnlyList<TargetCandidate<T>> candidates)
    {
        TargetCandidate<T> best = candidates[0];
        for (int i = 1; i < candidates.Count; i++)
        {
            TargetCandidate<T> candidate = candidates[i];
            bool better = candidate.Healing > best.Healing;
            bool tieButCloser = candidate.Healing == best.Healing && candidate.Distance < best.Distance;
            if (better || tieButCloser) best = candidate;
        }
        return best.Subject;
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
