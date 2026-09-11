using UnityEngine;

// Shared preamble for every IMGUI OnGUI in the project: apply the UI
// scale, and keep Tab out of IMGUI's focus traversal so it never lands
// in a text field - Tab is the tab-targeting key.
public static class DevGui
{
    public static void Begin()
    {
        UIScale.Apply();

        Event e = Event.current;
        if (e.isKey && e.keyCode == KeyCode.Tab) e.Use();
    }
}
