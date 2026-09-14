using UnityEngine;

// Player camera zoom distance, saved to the profile - mirrors
// UIScale/LookSensitivityScale's pattern. 0 (the profile's default)
// means "never set - use whatever distance PlayerCamera's prefab
// authored" (see PlayerCamera.Awake).
public static class CameraZoomScale
{
    public const float Min = 1.5f;
    public const float Max = 12f;

    public static float Value
    {
        get => ProfileStore.Current.CameraZoomDistance;
        set => ProfileStore.Current.CameraZoomDistance = Mathf.Clamp(value, Min, Max);
    }
}
