using System;
using System.Collections.Generic;
using System.Text;

// Character-name validation shared by the Lobby panel (to enable the Ready
// button - length rules only, it can't see other players' names) and
// LobbyState's server-side SubmitReady (the authoritative check, uniqueness
// included). Pure: the reason strings are what the Lobby panel shows under
// the name field.
public static class NameRules
{
    public const int MaxLength = 16;
    // LobbyEntry.Name is a FixedString64Bytes, whose UTF-8 capacity is 61
    // bytes (3 go to its length header) - counted here so a name of
    // multi-byte characters can't silently truncate when stored.
    public const int MaxUtf8Bytes = 61;

    // Null = accepted (normalized holds the trimmed name to store); otherwise
    // the reason to show. otherNames may be null when only the shape of the
    // name matters (client-side pre-check).
    public static string Validate(string name, IEnumerable<string> otherNames, out string normalized)
    {
        normalized = (name ?? "").Trim();
        if (normalized.Length == 0) return "Enter a name";
        if (CountCodePoints(normalized) > MaxLength) return $"Names are at most {MaxLength} characters";
        if (Encoding.UTF8.GetByteCount(normalized) > MaxUtf8Bytes) return "That name uses too many special characters";

        if (otherNames != null)
        {
            foreach (string other in otherNames)
            {
                if (other == null) continue;
                if (string.Equals(other.Trim(), normalized, StringComparison.OrdinalIgnoreCase)) return "That name is already taken";
            }
        }
        return null;
    }

    public static bool IsWellFormed(string name) => Validate(name, null, out _) == null;

    // string.Length counts UTF-16 code units, which would make a 16-emoji
    // name read as 32 "characters" - count code points so the character
    // limit means what a player expects, and leave the storage limit to the
    // byte check above.
    private static int CountCodePoints(string text)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
            count++;
        }
        return count;
    }
}
