using UnityEngine;

// Rebindable non-ability actions (the ability slots have their own keys).
public enum MovementAction { Forward, Backward, StrafeLeft, StrafeRight, Jump, AutoRun, AutoAttack }

public static class MovementInput
{
    // Indexed by MovementAction, so this order must match the enum's.
    public static readonly KeyCode[] Defaults =
    {
        KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D, KeyCode.Space, KeyCode.Backslash, KeyCode.T,
    };

    public static KeyCode KeyFor(MovementAction action) => ProfileStore.Current.MovementKeys[(int)action];
    public static bool IsHeld(MovementAction action) => Input.GetKey(KeyFor(action));
    public static bool WasPressed(MovementAction action) => Input.GetKeyDown(KeyFor(action));
}
