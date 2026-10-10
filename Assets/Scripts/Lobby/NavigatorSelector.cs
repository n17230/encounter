using System.Collections.Generic;

// Picks the lobby navigator from the eligible client ids with an injected
// roll (UnityEngine.Random.value at the call site), so the choice itself is
// pure and testable - same shape as OrbDropSelector.
public static class NavigatorSelector
{
    // "Nobody" - no connected client ever has this id (NGO's server id is 0
    // and client ids count up from 1).
    public const ulong None = ulong.MaxValue;

    public static ulong Pick(IReadOnlyList<ulong> candidates, float roll)
    {
        if (candidates == null || candidates.Count == 0) return None;

        // Random.value can return exactly 1.0, which would index one past the
        // end - clamped to the last candidate rather than special-cased.
        int index = (int)(roll * candidates.Count);
        if (index < 0) index = 0;
        if (index >= candidates.Count) index = candidates.Count - 1;
        return candidates[index];
    }
}
