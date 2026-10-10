using System;
using System.Collections.Generic;

// Pure ordering/filtering for the "every player's picks" sections on the
// Skills and Equipment panels: rows sort by clientId ascending with the
// local player in place (no self-first rule - same canonical order the
// lobby list and party frames use), and a synced ';'-joined Id string
// renders only the Ids that still resolve.
public static class PicksLayout
{
    public static void SortByClientId<T>(List<T> rows, Func<T, ulong> clientIdOf)
    {
        rows.Sort((a, b) => clientIdOf(a).CompareTo(clientIdOf(b)));
    }

    // Empty segments (an empty slot) and Ids isKnown rejects (renamed/removed
    // content, or a malformed string) are skipped rather than rendered as a
    // blank icon.
    public static List<string> ResolveIds(string joined, Func<string, bool> isKnown)
    {
        List<string> ids = new List<string>();
        if (string.IsNullOrEmpty(joined)) return ids;
        foreach (string id in joined.Split(';'))
        {
            if (id.Length == 0 || !isKnown(id)) continue;
            ids.Add(id);
        }
        return ids;
    }
}
