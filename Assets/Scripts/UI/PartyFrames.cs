using System.Collections.Generic;
using UnityEngine;

// Health/mana of every other connected player, WoW-party-style. Every
// client computes the same ordering (sorted by OwnerClientId, which is
// server-assigned and identical everywhere) so everyone's frames line up
// in the same order; each viewer's own entry is simply skipped rather than
// left as a gap, so the remaining players fill up from the top.
public static class PartyFrames
{
    private const float RowWidth = 200f;
    private const float NameHeight = 16f;
    private const float BarHeight = 14f;
    private const float RowHeight = NameHeight + BarHeight * 2f;
    private const float Margin = 12f;
    private const float Gap = 6f;

    private static readonly List<CharacterStats> party = new List<CharacterStats>();

    public static void Draw(IReadOnlyList<Targetable> allTargetables, ulong localClientId)
    {
        party.Clear();
        foreach (Targetable candidate in allTargetables)
        {
            if (candidate == null) continue;
            if (candidate.GetComponent<PlayerMovement>() == null) continue; // players only
            if (candidate.Stats == null) continue;
            party.Add(candidate.Stats);
        }
        party.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        GUIStyle nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 11 };
        GUIStyle barLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };

        float x = UIScale.Width - RowWidth - Margin;
        float y = Margin;

        for (int i = 0; i < party.Count; i++)
        {
            CharacterStats member = party[i];
            if (member.OwnerClientId == localClientId) continue; // that's you - the rest just fill up

            GUI.Label(new Rect(x, y, RowWidth, NameHeight), $"Player {i + 1}", nameStyle);
            DrawBar(x, y + NameHeight, RowWidth, BarHeight, member.CurrentHealth.Value, member.SyncedMaxHealth.Value, Color.red, barLabel);
            DrawBar(x, y + NameHeight + BarHeight, RowWidth, BarHeight, member.CurrentMana.Value, member.SyncedMaxMana.Value, Color.blue, barLabel);

            y += RowHeight + Gap;
        }
    }

    private static void DrawBar(float x, float y, float width, float height, float current, float max, Color fillColor, GUIStyle label)
    {
        Rect background = new Rect(x, y, width, height);
        GUI.Box(background, GUIContent.none);

        float pct = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        Rect fill = new Rect(x, y, width * pct, height);
        Color previous = GUI.color;
        GUI.color = fillColor;
        GUI.DrawTexture(fill, Texture2D.whiteTexture);
        GUI.color = previous;

        GUI.Label(background, $"{current:0}/{max:0}", label);
    }
}
