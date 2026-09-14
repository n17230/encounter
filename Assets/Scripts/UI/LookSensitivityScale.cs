using UnityEngine;

// Player-adjustable multiplier applied on top of every hardcoded
// look-sensitivity field (PlayerCamera's pitch/free-look yaw,
// PlayerMovement's turn yaw) - mirrors UIScale's pattern exactly.
// 1 = the project's original, untouched feel.
public static class LookSensitivityScale
{
    public const float Min = 0.25f;
    public const float Max = 3f;
    public const float Step = 0.25f;

    public static float Value
    {
        get => ProfileStore.Current.LookSensitivity;
        set => ProfileStore.Current.LookSensitivity = Mathf.Clamp(value, Min, Max);
    }
}
