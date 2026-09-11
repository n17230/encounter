using System.Collections.Generic;
using UnityEngine;

public static class TestingAreaGate
{
    public static bool Entered;
}

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

// Testing-lobby-scoped kit picker: up to 8 unique abilities, each with a
// player-chosen key binding from a fixed allowed set. Not the real
// player-assignable loadout system from DESIGN_IDEAS.md - that needs a
// proper item/ability unlock system first.
public static class LoadoutSelection
{
    public const int MaxSlots = 8;
    public static readonly AbilityData[] SlotAbilities = new AbilityData[MaxSlots];
    public static readonly KeyBindingOption?[] SlotKeys = new KeyBindingOption?[MaxSlots];
}

// Testing-lobby-scoped gear picker: one item per GearSlot. Not the real
// persistent/unlock-gated equipment system from DESIGN_IDEAS.md.
public static class GearSelection
{
    public static readonly ItemData[] EquippedItems = new ItemData[System.Enum.GetValues(typeof(GearSlot)).Length];
}

public enum MovementAction { Forward, Backward, StrafeLeft, StrafeRight, Jump, AutoRun }

// Owner-local movement key bindings, rebindable from the in-game menu. Same
// static/resets-on-restart scoping as LoadoutSelection - not persisted.
public static class MovementBindings
{
    // Indexed by MovementAction, so this order must match the enum's.
    private static readonly KeyCode[] Defaults =
    {
        KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D, KeyCode.Space, KeyCode.Backslash,
    };

    public static readonly KeyCode[] Keys = (KeyCode[])Defaults.Clone();

    public static bool IsHeld(MovementAction action) => Input.GetKey(Keys[(int)action]);
    public static bool WasPressed(MovementAction action) => Input.GetKeyDown(Keys[(int)action]);

    public static void ResetToDefaults()
    {
        System.Array.Copy(Defaults, Keys, Keys.Length);
    }
}

// Uniform scale for all of the IMGUI dev UI. Every OnGUI in the project
// calls Apply() first, then lays out against Width/Height instead of
// Screen.width/height so edge-anchored elements stay on the edges.
public static class UIScale
{
    public const float Min = 0.75f;
    public const float Max = 2.5f;
    public const float Step = 0.25f;

    public static float Value = 1f;

    public static float Width => Screen.width / Value;
    public static float Height => Screen.height / Value;

    public static void Apply()
    {
        GUI.matrix = Matrix4x4.Scale(new Vector3(Value, Value, 1f));
    }
}

public class MainMenu : MonoBehaviour
{
    private enum Panel { None, Skills, Gear, Options }

    private static readonly KeyBindingOption[] AllowedKeyBindings = BuildAllowedKeyBindings();
    private static readonly KeyCode[] AllKeyCodes = (KeyCode[])System.Enum.GetValues(typeof(KeyCode));
    private static readonly string[] MovementActionNames = System.Enum.GetNames(typeof(MovementAction));

    // True while the in-game Escape menu is up. Player scripts check this to
    // ignore gameplay input (movement, mouse-look, click-targeting, casting)
    // so interacting with the menu never leaks into the world.
    public static bool IsOpen { get; private set; }

    // Fired when the in-game menu closes, so owner-side player components can
    // re-send any skill/gear changes to the server.
    public static event System.Action Closed;

    [SerializeField] private AbilityData[] availableAbilities;
    [SerializeField] private ItemData[] availableGear;

    private Panel activePanel = Panel.None;
    private int selectedSlot = -1;
    private int awaitingKeyForSlot = -1;
    private int awaitingKeyForMovement = -1;
    private Vector2 scrollPosition;

    private static KeyBindingOption[] BuildAllowedKeyBindings()
    {
        List<KeyBindingOption> options = new List<KeyBindingOption>();
        KeyCode[] numberKeys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5 };
        foreach (KeyCode key in numberKeys) options.Add(new KeyBindingOption(key, false));
        foreach (KeyCode key in numberKeys) options.Add(new KeyBindingOption(key, true));

        KeyCode[] fKeys = { KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F5 };
        foreach (KeyCode key in fKeys) options.Add(new KeyBindingOption(key, false));

        KeyCode[] letterKeys = { KeyCode.Q, KeyCode.E, KeyCode.R, KeyCode.T, KeyCode.F, KeyCode.G };
        foreach (KeyCode key in letterKeys) options.Add(new KeyBindingOption(key, false));

        return options.ToArray();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            HandleEscape();
            return;
        }

        if (awaitingKeyForSlot >= 0)
        {
            CaptureAbilityKey();
        }
        else if (awaitingKeyForMovement >= 0)
        {
            CaptureMovementKey();
        }
    }

    private void HandleEscape()
    {
        // Escape while waiting on a key press just cancels the capture,
        // rather than also closing the menu out from under the player.
        if (awaitingKeyForSlot >= 0 || awaitingKeyForMovement >= 0)
        {
            awaitingKeyForSlot = -1;
            awaitingKeyForMovement = -1;
            return;
        }

        if (!TestingAreaGate.Entered) return;

        if (IsOpen) CloseInGameMenu();
        else OpenInGameMenu();
    }

    private void OpenInGameMenu()
    {
        IsOpen = true;
        activePanel = Panel.None;
        selectedSlot = -1;
        scrollPosition = Vector2.zero;
    }

    private void CloseInGameMenu()
    {
        IsOpen = false;
        activePanel = Panel.None;
        selectedSlot = -1;
        awaitingKeyForSlot = -1;
        awaitingKeyForMovement = -1;
        Closed?.Invoke();
    }

    private void CaptureAbilityKey()
    {
        foreach (KeyBindingOption option in AllowedKeyBindings)
        {
            if (!option.WasPressedThisFrame()) continue;

            for (int i = 0; i < LoadoutSelection.MaxSlots; i++)
            {
                if (LoadoutSelection.SlotKeys[i].HasValue && LoadoutSelection.SlotKeys[i].Value.Matches(option))
                {
                    LoadoutSelection.SlotKeys[i] = null;
                }
            }
            if (!option.RequiresShift) UnbindMovementKey(option.Key);

            LoadoutSelection.SlotKeys[awaitingKeyForSlot] = option;
            awaitingKeyForSlot = -1;
            break;
        }
    }

    private void CaptureMovementKey()
    {
        foreach (KeyCode key in AllKeyCodes)
        {
            // Mouse buttons and joystick codes sit at the end of the enum;
            // clicking the "Press a key..." button itself must not bind Mouse0.
            if (key == KeyCode.None || key >= KeyCode.Mouse0) continue;
            if (!Input.GetKeyDown(key)) continue;

            UnbindMovementKey(key);
            for (int i = 0; i < LoadoutSelection.MaxSlots; i++)
            {
                KeyBindingOption? slotKey = LoadoutSelection.SlotKeys[i];
                if (slotKey.HasValue && slotKey.Value.Key == key && !slotKey.Value.RequiresShift)
                {
                    LoadoutSelection.SlotKeys[i] = null;
                }
            }

            MovementBindings.Keys[awaitingKeyForMovement] = key;
            awaitingKeyForMovement = -1;
            return;
        }
    }

    private static void UnbindMovementKey(KeyCode key)
    {
        for (int i = 0; i < MovementBindings.Keys.Length; i++)
        {
            if (MovementBindings.Keys[i] == key) MovementBindings.Keys[i] = KeyCode.None;
        }
    }

    private void OnGUI()
    {
        if (TestingAreaGate.Entered && !IsOpen) return;

        UIScale.Apply();
        switch (activePanel)
        {
            case Panel.None:
                if (TestingAreaGate.Entered) DrawInGamePanel();
                else DrawMainPanel();
                break;
            case Panel.Skills:
                DrawSkillsPanel();
                break;
            case Panel.Gear:
                DrawGearPanel();
                break;
            case Panel.Options:
                DrawOptionsPanel();
                break;
        }

        DrawTooltip();
    }

    private static void DrawTooltip()
    {
        if (string.IsNullOrEmpty(GUI.tooltip)) return;

        GUIContent content = new GUIContent(GUI.tooltip);
        Vector2 size = GUI.skin.box.CalcSize(content);
        Vector2 mouse = Event.current.mousePosition;
        GUI.Box(new Rect(mouse.x + 16, mouse.y + 16, size.x + 12, size.y + 12), GUI.tooltip);
    }

    private static string BuildAbilityTooltip(AbilityData ability)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine(ability.AbilityName);
        if (ability.Damage > 0f) sb.AppendLine($"Damage: {ability.Damage}");
        sb.AppendLine($"Mana Cost: {ability.ManaCost}");
        sb.AppendLine($"Cooldown: {ability.Cooldown}s");
        if (ability.CastTime > 0f) sb.AppendLine($"Cast Time: {ability.CastTime}s");
        sb.AppendLine($"Range: {ability.Range}");

        if (ability.Debuff == DebuffType.Burn)
        {
            sb.AppendLine($"Burn: {ability.DebuffMagnitude}/sec for {ability.DebuffDuration}s");
        }
        else if (ability.Debuff == DebuffType.Slow)
        {
            sb.AppendLine($"Slow: -{ability.DebuffMagnitude * 100f}% speed for {ability.DebuffDuration}s");
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildItemTooltip(ItemData item)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine(item.ItemName);
        sb.AppendLine($"Slot: {item.Slot}");

        foreach (StatBonus bonus in item.Bonuses)
        {
            bool isPercent = bonus.ModifierType == StatModifierType.PercentAdditive;
            float displayValue = isPercent ? bonus.Value * 100f : bonus.Value;
            string sign = displayValue >= 0f ? "+" : "";
            sb.AppendLine($"{sign}{displayValue}{(isPercent ? "%" : "")} {bonus.Stat}");
        }

        return sb.ToString().TrimEnd();
    }

    private void OpenPanel(Panel panel)
    {
        activePanel = panel;
        scrollPosition = Vector2.zero;
    }

    private void DrawMainPanel()
    {
        GUILayout.BeginArea(new Rect(UIScale.Width / 2f - 100, UIScale.Height / 2f - 110, 200, 220));
        if (GUILayout.Button("Choose Skills", GUILayout.Height(40))) OpenPanel(Panel.Skills);
        if (GUILayout.Button("Choose Gear", GUILayout.Height(40))) OpenPanel(Panel.Gear);
        if (GUILayout.Button("Options", GUILayout.Height(40))) OpenPanel(Panel.Options);
        if (GUILayout.Button("Enter Testing Area", GUILayout.Height(40)))
        {
            TestingAreaGate.Entered = true;
        }
        GUILayout.EndArea();
    }

    private void DrawInGamePanel()
    {
        GUILayout.BeginArea(new Rect(UIScale.Width / 2f - 100, UIScale.Height / 2f - 110, 200, 220));
        if (GUILayout.Button("Skills", GUILayout.Height(40))) OpenPanel(Panel.Skills);
        if (GUILayout.Button("Gear", GUILayout.Height(40))) OpenPanel(Panel.Gear);
        if (GUILayout.Button("Options", GUILayout.Height(40))) OpenPanel(Panel.Options);
        if (GUILayout.Button("Resume", GUILayout.Height(40))) CloseInGameMenu();
        GUILayout.EndArea();
    }

    private void DrawSkillsPanel()
    {
        GUILayout.BeginArea(new Rect(UIScale.Width / 2f - 320, UIScale.Height / 2f - 220, 640, 440));
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(400));

        GUILayout.Label("Choose Your Skills");
        GUILayout.BeginHorizontal();

        GUILayout.BeginVertical(GUILayout.Width(300));
        GUILayout.Label("Available Abilities");
        foreach (AbilityData ability in availableAbilities)
        {
            if (ability == null) continue;
            if (System.Array.IndexOf(LoadoutSelection.SlotAbilities, ability) >= 0) continue;

            if (GUILayout.Button(new GUIContent(ability.AbilityName, BuildAbilityTooltip(ability))))
            {
                int emptySlot = System.Array.IndexOf(LoadoutSelection.SlotAbilities, null);
                if (emptySlot >= 0)
                {
                    LoadoutSelection.SlotAbilities[emptySlot] = ability;
                }
            }
        }
        GUILayout.EndVertical();

        GUILayout.BeginVertical(GUILayout.Width(300));
        GUILayout.Label("Your Kit (8 slots)");
        for (int i = 0; i < LoadoutSelection.MaxSlots; i++)
        {
            AbilityData slotAbility = LoadoutSelection.SlotAbilities[i];
            string keyLabel = LoadoutSelection.SlotKeys[i].HasValue ? LoadoutSelection.SlotKeys[i].Value.DisplayName : "Unbound";
            string label = slotAbility != null ? $"{i + 1}. {slotAbility.AbilityName} [{keyLabel}]" : $"{i + 1}. (empty)";

            if (slotAbility == null)
            {
                GUILayout.Label(label);
                continue;
            }

            if (GUILayout.Button(new GUIContent(label, BuildAbilityTooltip(slotAbility))))
            {
                selectedSlot = selectedSlot == i ? -1 : i;
                awaitingKeyForSlot = -1;
            }

            if (selectedSlot == i)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Remove"))
                {
                    LoadoutSelection.SlotAbilities[i] = null;
                    LoadoutSelection.SlotKeys[i] = null;
                    selectedSlot = -1;
                }
                if (GUILayout.Button(awaitingKeyForSlot == i ? "Press a key..." : "Set Key Binding"))
                {
                    awaitingKeyForSlot = awaitingKeyForSlot == i ? -1 : i;
                }
                GUILayout.EndHorizontal();
            }
        }
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
        GUILayout.EndScrollView();

        if (GUILayout.Button("Back"))
        {
            activePanel = Panel.None;
            selectedSlot = -1;
            awaitingKeyForSlot = -1;
        }
        GUILayout.EndArea();
    }

    private void DrawGearPanel()
    {
        GUILayout.BeginArea(new Rect(UIScale.Width / 2f - 200, UIScale.Height / 2f - 260, 400, 520));
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(480));

        GUILayout.Label("Choose Your Gear");
        foreach (GearSlot slot in System.Enum.GetValues(typeof(GearSlot)))
        {
            int slotIndex = (int)slot;
            ItemData equipped = GearSelection.EquippedItems[slotIndex];

            GUILayout.BeginHorizontal();
            string slotLabel = $"{slot}: {(equipped != null ? equipped.ItemName : "(empty)")}";
            GUILayout.Label(new GUIContent(slotLabel, equipped != null ? BuildItemTooltip(equipped) : null), GUILayout.Width(220));

            if (equipped != null)
            {
                if (GUILayout.Button("Unequip", GUILayout.Width(80)))
                {
                    GearSelection.EquippedItems[slotIndex] = null;
                }
            }
            else
            {
                foreach (ItemData item in availableGear)
                {
                    if (item == null || item.Slot != slot) continue;
                    if (System.Array.IndexOf(GearSelection.EquippedItems, item) >= 0) continue;

                    if (GUILayout.Button(new GUIContent(item.ItemName, BuildItemTooltip(item)), GUILayout.Width(100)))
                    {
                        GearSelection.EquippedItems[slotIndex] = item;
                    }
                }
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();

        if (GUILayout.Button("Back"))
        {
            activePanel = Panel.None;
        }
        GUILayout.EndArea();
    }

    private void DrawOptionsPanel()
    {
        GUILayout.BeginArea(new Rect(UIScale.Width / 2f - 150, UIScale.Height / 2f - 170, 300, 340));

        GUILayout.Label("UI Scale");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("-", GUILayout.Width(40)))
        {
            UIScale.Value = Mathf.Max(UIScale.Min, UIScale.Value - UIScale.Step);
        }
        GUIStyle centered = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
        GUILayout.Label($"{UIScale.Value * 100f:0}%", centered);
        if (GUILayout.Button("+", GUILayout.Width(40)))
        {
            UIScale.Value = Mathf.Min(UIScale.Max, UIScale.Value + UIScale.Step);
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(10);
        GUILayout.Label("Movement Keybindings");

        for (int i = 0; i < MovementBindings.Keys.Length; i++)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(MovementActionNames[i], GUILayout.Width(120));

            string keyLabel;
            if (awaitingKeyForMovement == i) keyLabel = "Press a key...";
            else if (MovementBindings.Keys[i] == KeyCode.None) keyLabel = "Unbound";
            else keyLabel = MovementBindings.Keys[i].ToString();

            if (GUILayout.Button(keyLabel))
            {
                awaitingKeyForMovement = awaitingKeyForMovement == i ? -1 : i;
                awaitingKeyForSlot = -1;
            }
            GUILayout.EndHorizontal();
        }

        if (GUILayout.Button("Reset to Defaults"))
        {
            MovementBindings.ResetToDefaults();
            awaitingKeyForMovement = -1;
        }

        if (GUILayout.Button("Back"))
        {
            activePanel = Panel.None;
            awaitingKeyForMovement = -1;
        }
        GUILayout.EndArea();
    }
}
