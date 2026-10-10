using System.Collections.Generic;
using UnityEngine;

// Health/mana of every other connected player, WoW-party-style. Every
// client computes the same ordering (sorted by OwnerClientId, which is
// server-assigned and identical everywhere) so everyone's frames line up
// in the same order; each viewer's own entry is simply skipped rather than
// left as a gap, so the remaining players fill up from the top.
//
// GetDisplayOrder is also what PlayerTargeting's F1-F5 party-targeting
// keys read: F1 targets whoever is drawn in the first row on THIS
// viewer's screen, F2 the second, and so on - a viewer-relative visual
// position, distinct from PartyNumber below (each player's stable,
// identical-for-everyone identity, e.g. always "Player 3" no matter whose
// screen you're reading it from - it just may sit in a different row for
// different viewers depending on who they skip).
public static class PartyFrames
{
    public readonly struct Slot
    {
        public readonly Targetable Member;
        public readonly int PartyNumber;

        public Slot(Targetable member, int partyNumber)
        {
            Member = member;
            PartyNumber = partyNumber;
        }
    }

    private const float RowWidth = 200f;
    private const float NameHeight = 16f;
    private const float BarHeight = 14f;
    private const float RowHeight = NameHeight + BarHeight * 2f;
    private const float Margin = 12f;
    private const float Gap = 6f;

    private static readonly List<Targetable> party = new List<Targetable>();
    private static readonly List<Slot> displayOrder = new List<Slot>();

    public static IReadOnlyList<Slot> GetDisplayOrder(IReadOnlyList<Targetable> allTargetables, ulong localClientId)
    {
        party.Clear();
        foreach (Targetable candidate in allTargetables)
        {
            if (candidate == null) continue;
            if (candidate.GetComponent<PlayerMovement>() == null) continue; // players only
            if (candidate.Stats == null) continue;
            party.Add(candidate);
        }
        party.Sort((a, b) => a.Stats.OwnerClientId.CompareTo(b.Stats.OwnerClientId));

        displayOrder.Clear();
        for (int i = 0; i < party.Count; i++)
        {
            Targetable member = party[i];
            if (member.Stats.OwnerClientId == localClientId) continue; // that's you - the rest fill up
            displayOrder.Add(new Slot(member, i + 1));
        }
        return displayOrder;
    }

    // Click-to-target hit test (see PlayerTargeting's left-click): which
    // drawn row (0-based, in GetDisplayOrder's order) a GUI-space point
    // falls on, or -1 for none - left or right of the column, above the
    // first row, in the gap between two rows, or past the last one. Pure:
    // the same layout constants Draw lays out with, with the GUI width
    // passed in rather than read from UIScale (which reads Screen, which a
    // test can't drive). Edges follow Rect.Contains: min inclusive, max
    // exclusive.
    public static int RowIndexAt(Vector2 guiPoint, float guiWidth, int rowCount)
    {
        float x = guiWidth - RowWidth - Margin;
        if (guiPoint.x < x || guiPoint.x >= x + RowWidth) return -1;

        float relativeY = guiPoint.y - Margin;
        if (relativeY < 0f) return -1;

        float pitch = RowHeight + Gap;
        int row = (int)(relativeY / pitch);
        if (row >= rowCount) return -1;
        if (relativeY - row * pitch >= RowHeight) return -1; // in the gap below that row
        return row;
    }

    // Input.mousePosition (origin bottom-left, in screen pixels) to the
    // space Draw lays out in (origin top-left, divided by the UI scale that
    // DevGui.Begin applies via GUI.matrix) - the inverse of what IMGUI does
    // to every rect it draws, so RowIndexAt can compare like with like.
    public static Vector2 ScreenToGui(Vector2 mouse, float screenHeight, float scale)
    {
        return new Vector2(mouse.x / scale, (screenHeight - mouse.y) / scale);
    }

    public static void Draw(IReadOnlyList<Targetable> allTargetables, ulong localClientId)
    {
        IReadOnlyList<Slot> ordered = GetDisplayOrder(allTargetables, localClientId);

        GUIStyle nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 11 };
        GUIStyle barLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };

        float x = UIScale.Width - RowWidth - Margin;
        float y = Margin;

        foreach (Slot slot in ordered)
        {
            CharacterStats member = slot.Member.Stats;

            // The lobby name when the player has one; "Player N" only for a
            // player that never went through the lobby (see PlayerIdentity).
            string name = PlayerIdentity.NameOf(slot.Member) ?? $"Player {slot.PartyNumber}";
            GUI.Label(new Rect(x, y, RowWidth, NameHeight), name, nameStyle);
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
