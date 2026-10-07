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

    public string DisplayName
    {
        get
        {
            // KeyCode.Alpha0-9.ToString() reads as "Alpha1" - show just the
            // digit instead, same as every other game's keybind label.
            string keyText = Key >= KeyCode.Alpha0 && Key <= KeyCode.Alpha9
                ? ((int)(Key - KeyCode.Alpha0)).ToString()
                : Key.ToString();
            return RequiresShift ? $"Shift+{keyText}" : keyText;
        }
    }

    public bool WasPressedThisFrame()
    {
        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (RequiresShift != shiftHeld) return false;
        return Input.GetKeyDown(Key);
    }

    public bool Matches(KeyBindingOption other) => Key == other.Key && RequiresShift == other.RequiresShift;
}
