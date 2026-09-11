using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Pregame menu (skills / gear / options / enter) and, once in the testing
// area, the Escape menu that reuses the same panels. All choices are read
// from and written to ProfileStore.Current, which persists across restarts.
public class MainMenu : MonoBehaviour
{
    private enum Panel { None, Skills, Gear, Options, Summon }

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

    private Panel activePanel = Panel.None;
    private int selectedSlot = -1;
    private int awaitingKeyForSlot = -1;
    private int awaitingKeyForMovement = -1;
    private Vector2 scrollPosition;
    private int summonMobIndex;
    private int summonCount = 1;

    private static PlayerProfile Profile => ProfileStore.Current;

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

        if (IsOpen)
        {
            CloseInGameMenu();
            return;
        }

        // MMO convention: Escape first drops the current target; only with
        // nothing targeted does it open the menu.
        PlayerTargeting targeting = LocalPlayer<PlayerTargeting>();
        if (targeting != null && targeting.CurrentTarget != null)
        {
            targeting.ClearTarget();
            return;
        }

        OpenInGameMenu();
    }

    private static T LocalPlayer<T>() where T : Component
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager.LocalClient == null || manager.LocalClient.PlayerObject == null) return null;
        return manager.LocalClient.PlayerObject.GetComponent<T>();
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
        ProfileStore.Save();
        Closed?.Invoke();
    }

    private void LeavePanel()
    {
        activePanel = Panel.None;
        selectedSlot = -1;
        awaitingKeyForSlot = -1;
        awaitingKeyForMovement = -1;
        ProfileStore.Save();
    }

    private void CaptureAbilityKey()
    {
        foreach (KeyBindingOption option in AllowedKeyBindings)
        {
            if (!option.WasPressedThisFrame()) continue;

            for (int i = 0; i < PlayerProfile.AbilitySlots; i++)
            {
                KeyBindingOption? existing = Profile.GetSlotKey(i);
                if (existing.HasValue && existing.Value.Matches(option)) Profile.SetSlotKey(i, null);
            }
            if (!option.RequiresShift) UnbindMovementKey(option.Key);

            Profile.SetSlotKey(awaitingKeyForSlot, option);
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
            for (int i = 0; i < PlayerProfile.AbilitySlots; i++)
            {
                KeyBindingOption? slotKey = Profile.GetSlotKey(i);
                if (slotKey.HasValue && slotKey.Value.Key == key && !slotKey.Value.RequiresShift)
                {
                    Profile.SetSlotKey(i, null);
                }
            }

            Profile.MovementKeys[awaitingKeyForMovement] = key;
            awaitingKeyForMovement = -1;
            return;
        }
    }

    private static void UnbindMovementKey(KeyCode key)
    {
        KeyCode[] keys = Profile.MovementKeys;
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] == key) keys[i] = KeyCode.None;
        }
    }

    private void OnGUI()
    {
        if (TestingAreaGate.Entered && !IsOpen) return;

        DevGui.Begin();
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
            case Panel.Summon:
                DrawSummonPanel();
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

        if (ability.Effect != null)
        {
            float duration = ability.DirectHitEffectDuration > 0f ? ability.DirectHitEffectDuration : ability.Effect.Duration;
            sb.AppendLine(DescribeEffect(ability.Effect, duration));
        }

        return sb.ToString().TrimEnd();
    }

    private static string DescribeEffect(StatusEffectData effect, float duration)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append(effect.DisplayName).Append(':');
        if (effect.TickDamage > 0f) sb.Append($" {effect.TickDamage} dmg/{effect.TickInterval}s");
        foreach (StatBonus bonus in effect.Modifiers)
        {
            bool isPercent = bonus.ModifierType == StatModifierType.PercentAdditive;
            float displayValue = isPercent ? bonus.Value * 100f : bonus.Value;
            string sign = displayValue >= 0f ? "+" : "";
            sb.Append($" {sign}{displayValue}{(isPercent ? "%" : "")} {bonus.Stat}");
        }
        sb.Append($" for {duration}s");
        return sb.ToString();
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
            ProfileStore.Save();
            TestingAreaGate.Entered = true;
        }
        GUILayout.EndArea();
    }

    private void DrawInGamePanel()
    {
        GUILayout.BeginArea(new Rect(UIScale.Width / 2f - 100, UIScale.Height / 2f - 130, 200, 260));
        if (GUILayout.Button("Skills", GUILayout.Height(40))) OpenPanel(Panel.Skills);
        if (GUILayout.Button("Gear", GUILayout.Height(40))) OpenPanel(Panel.Gear);
        if (GUILayout.Button("Summon Mobs", GUILayout.Height(40))) OpenPanel(Panel.Summon);
        if (GUILayout.Button("Options", GUILayout.Height(40))) OpenPanel(Panel.Options);
        if (GUILayout.Button("Resume", GUILayout.Height(40))) CloseInGameMenu();
        GUILayout.EndArea();
    }

    // Testing-lobby tool: spawn mobs to fight. Buttons rather than a text
    // field for the count, because IMGUI's native Tab focus traversal grabs
    // any focusable control and Tab is the tab-targeting key.
    private void DrawSummonPanel()
    {
        GUILayout.BeginArea(new Rect(UIScale.Width / 2f - 120, UIScale.Height / 2f - 150, 240, 300));
        GUILayout.Label("Summon Mobs");

        PlayerSummon summon = LocalPlayer<PlayerSummon>();
        if (summon == null || summon.SummonableMobs.Count == 0)
        {
            GUILayout.Label("No player spawned yet.");
        }
        else
        {
            for (int i = 0; i < summon.SummonableMobs.Count; i++)
            {
                string label = (i == summonMobIndex ? "> " : "") + PlayerSummon.MobLabel(summon.SummonableMobs[i]);
                if (GUILayout.Button(label)) summonMobIndex = i;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Count:", GUILayout.Width(50));
            if (GUILayout.Button("-", GUILayout.Width(30))) summonCount = Mathf.Max(1, summonCount - 1);
            GUIStyle centered = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            GUILayout.Label(summonCount.ToString(), centered, GUILayout.Width(40));
            if (GUILayout.Button("+", GUILayout.Width(30))) summonCount = Mathf.Min(summon.MaxSummonCount, summonCount + 1);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Summon", GUILayout.Height(30)))
            {
                summon.RequestSummon(summonMobIndex, summonCount);
                CloseInGameMenu();
            }
        }

        if (GUILayout.Button("Back")) LeavePanel();
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
        foreach (AbilityData ability in GameDatabase.Abilities)
        {
            if (Profile.IndexOfAbility(ability) >= 0) continue;

            if (GUILayout.Button(new GUIContent(ability.AbilityName, BuildAbilityTooltip(ability))))
            {
                int emptySlot = System.Array.IndexOf(Profile.SlotAbilityIds, null);
                if (emptySlot < 0) emptySlot = System.Array.IndexOf(Profile.SlotAbilityIds, "");
                if (emptySlot >= 0) Profile.SetSlotAbility(emptySlot, ability);
            }
        }
        GUILayout.EndVertical();

        GUILayout.BeginVertical(GUILayout.Width(300));
        GUILayout.Label($"Your Kit ({PlayerProfile.AbilitySlots} slots)");
        for (int i = 0; i < PlayerProfile.AbilitySlots; i++)
        {
            AbilityData slotAbility = Profile.GetSlotAbility(i);
            KeyBindingOption? slotKey = Profile.GetSlotKey(i);
            string keyLabel = slotKey.HasValue ? slotKey.Value.DisplayName : "Unbound";
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
                    Profile.SetSlotAbility(i, null);
                    Profile.SetSlotKey(i, null);
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

        if (GUILayout.Button("Back")) LeavePanel();
        GUILayout.EndArea();
    }

    private void DrawGearPanel()
    {
        GUILayout.BeginArea(new Rect(UIScale.Width / 2f - 200, UIScale.Height / 2f - 260, 400, 520));
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(480));

        GUILayout.Label("Choose Your Gear");
        foreach (GearSlot slot in System.Enum.GetValues(typeof(GearSlot)))
        {
            ItemData equipped = Profile.GetGear(slot);

            GUILayout.BeginHorizontal();
            string slotLabel = $"{slot}: {(equipped != null ? equipped.ItemName : "(empty)")}";
            GUILayout.Label(new GUIContent(slotLabel, equipped != null ? BuildItemTooltip(equipped) : null), GUILayout.Width(220));

            if (equipped != null)
            {
                if (GUILayout.Button("Unequip", GUILayout.Width(80))) Profile.SetGear(slot, null);
            }
            else
            {
                foreach (ItemData item in GameDatabase.Items)
                {
                    if (item.Slot != slot || Profile.IsEquipped(item)) continue;

                    if (GUILayout.Button(new GUIContent(item.ItemName, BuildItemTooltip(item)), GUILayout.Width(100)))
                    {
                        Profile.SetGear(slot, item);
                    }
                }
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();

        if (GUILayout.Button("Back")) LeavePanel();
        GUILayout.EndArea();
    }

    private void DrawOptionsPanel()
    {
        GUILayout.BeginArea(new Rect(UIScale.Width / 2f - 150, UIScale.Height / 2f - 170, 300, 340));

        GUILayout.Label("UI Scale");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("-", GUILayout.Width(40))) UIScale.Value -= UIScale.Step;
        GUIStyle centered = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
        GUILayout.Label($"{UIScale.Value * 100f:0}%", centered);
        if (GUILayout.Button("+", GUILayout.Width(40))) UIScale.Value += UIScale.Step;
        GUILayout.EndHorizontal();

        GUILayout.Space(10);
        GUILayout.Label("Movement Keybindings");

        KeyCode[] keys = Profile.MovementKeys;
        for (int i = 0; i < keys.Length; i++)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(MovementActionNames[i], GUILayout.Width(120));

            string keyLabel;
            if (awaitingKeyForMovement == i) keyLabel = "Press a key...";
            else if (keys[i] == KeyCode.None) keyLabel = "Unbound";
            else keyLabel = keys[i].ToString();

            if (GUILayout.Button(keyLabel))
            {
                awaitingKeyForMovement = awaitingKeyForMovement == i ? -1 : i;
                awaitingKeyForSlot = -1;
            }
            GUILayout.EndHorizontal();
        }

        if (GUILayout.Button("Reset to Defaults"))
        {
            System.Array.Copy(MovementInput.Defaults, keys, keys.Length);
            awaitingKeyForMovement = -1;
        }

        if (GUILayout.Button("Back")) LeavePanel();
        GUILayout.EndArea();
    }
}
