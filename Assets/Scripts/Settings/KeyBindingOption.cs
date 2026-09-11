using UnityEngine;

public struct KeyBindingOption
{
    public KeyCode Key;
    public bool RequiresShift;

    public KeyBindingOption(KeyCode key, bool requiresShift)
    {
        Key = key;
        RequiresShift = requiresShift;
    }

    public string DisplayName => RequiresShift ? $"Shift+{Key}" : Key.ToString();

    public bool WasPressedThisFrame()
    {
        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (RequiresShift != shiftHeld) return false;
        return Input.GetKeyDown(Key);
    }

    public bool Matches(KeyBindingOption other) => Key == other.Key && RequiresShift == other.RequiresShift;
}
