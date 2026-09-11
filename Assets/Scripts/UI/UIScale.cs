using UnityEngine;

// Uniform scale for all of the IMGUI dev UI. Every OnGUI in the project
// calls Apply() first, then lays out against Width/Height instead of
// Screen.width/height so edge-anchored elements stay on the edges.
public static class UIScale
{
    public const float Min = 0.75f;
    public const float Max = 2.5f;
    public const float Step = 0.25f;

    public static float Value
    {
        get => ProfileStore.Current.UiScale;
        set => ProfileStore.Current.UiScale = Mathf.Clamp(value, Min, Max);
    }

    public static float Width => Screen.width / Value;
    public static float Height => Screen.height / Value;

    public static void Apply()
    {
        GUI.matrix = Matrix4x4.Scale(new Vector3(Value, Value, 1f));
    }
}
