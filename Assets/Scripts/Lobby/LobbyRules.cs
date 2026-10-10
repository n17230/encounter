using System.Collections.Generic;

// The admitted/ready bits of one lobby entry - what the pure phase rules
// below actually need, projected out of LobbyEntry by LobbyState so the
// rules (and their tests) never touch the network struct.
public readonly struct LobbyReadiness
{
    public readonly ulong ClientId;
    public readonly bool Admitted;
    public readonly bool Ready;

    public LobbyReadiness(ulong clientId, bool admitted, bool ready)
    {
        ClientId = clientId;
        Admitted = admitted;
        Ready = ready;
    }
}

// What a disconnect did to the navigator - see LobbyRules.ResolveNavigatorAfterLeave.
public enum NavigatorOutcome
{
    Unchanged, // the leaver wasn't the navigator
    Repicked,  // the leaver was; a new one was drawn from the remaining admitted
    NoneLeft   // the leaver was, and nobody admitted remains - back to MainMenu
}

// Pure lobby phase rules - LobbyState (server) is the only caller, these
// just make the transition table testable without a NetworkManager.
public static class LobbyRules
{
    // "Everyone who's in is ready" - waiters (not admitted) don't count
    // either way, and an empty lobby is never ready.
    public static bool AllReady(IReadOnlyList<LobbyReadiness> entries)
    {
        int admitted = 0;
        foreach (LobbyReadiness entry in entries)
        {
            if (!entry.Admitted) continue;
            admitted++;
            if (!entry.Ready) return false;
        }
        return admitted > 0;
    }

    // A client joining while the team is already picking (Choice/Spells/
    // Equipment) waits on the Lobby panel until the navigator sends everyone
    // back; joining before that, or after Start, goes straight in.
    public static bool IsAdmittedJoin(LobbyPhase phase)
    {
        return phase == LobbyPhase.MainMenu || phase == LobbyPhase.Started;
    }

    // The navigator's legal moves: from Choice into a picking screen or back
    // to the main-menu screen; from a picking screen only back to Choice
    // (Start and "Back to main menu" are Choice-only).
    public static bool IsLegalNavigation(LobbyPhase from, LobbyPhase to)
    {
        switch (from)
        {
            case LobbyPhase.Choice:
                return to == LobbyPhase.Spells || to == LobbyPhase.Equipment || to == LobbyPhase.MainMenu;
            case LobbyPhase.Spells:
            case LobbyPhase.Equipment:
                return to == LobbyPhase.Choice;
            default:
                return false;
        }
    }

    public static NavigatorOutcome ResolveNavigatorAfterLeave(ulong leaver, ulong navigator,
        IReadOnlyList<ulong> remainingAdmitted, float roll, out ulong newNavigator)
    {
        newNavigator = navigator;
        if (leaver != navigator) return NavigatorOutcome.Unchanged;

        newNavigator = NavigatorSelector.Pick(remainingAdmitted, roll);
        return newNavigator == NavigatorSelector.None ? NavigatorOutcome.NoneLeft : NavigatorOutcome.Repicked;
    }
}
