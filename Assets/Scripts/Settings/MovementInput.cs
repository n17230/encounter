using UnityEngine;

// Rebindable non-ability actions (the ability slots have their own keys).
// CycleTarget/SelfTarget/PartyTarget1-5/ZoomIn/ZoomOut were previously
// hardcoded (Tab/backtick/F1-F5/Up-Down arrow) - appended here (never
// inserted/reordered - MovementInput.KeyFor indexes MovementKeys by
// (int)action, so reordering would shift every existing saved profile's
// bindings onto the wrong action) so they're rebindable the same way
// movement already is.
public enum MovementAction
{
    Forward, Backward, StrafeLeft, StrafeRight, Jump, AutoRun, AutoAttack,
    CycleTarget, SelfTarget,
    PartyTarget1, PartyTarget2, PartyTarget3, PartyTarget4, PartyTarget5,
    ZoomIn, ZoomOut
}

public static class MovementInput
{
    // Indexed by MovementAction, so this order must match the enum's.
    public static readonly KeyCode[] Defaults =
    {
        KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D, KeyCode.Space, KeyCode.Backslash, KeyCode.T,
        KeyCode.Tab, KeyCode.BackQuote,
        KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F5,
        KeyCode.UpArrow, KeyCode.DownArrow,
    };

    public static KeyCode KeyFor(MovementAction action) => ProfileStore.Current.MovementKeys[(int)action];
    public static bool IsHeld(MovementAction action) => Input.GetKey(KeyFor(action));
    public static bool WasPressed(MovementAction action) => Input.GetKeyDown(KeyFor(action));
}
