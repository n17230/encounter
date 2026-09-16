using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

// Pregame menu (skills / gear / options / enter) and, once in the testing
// area, the Escape menu that reuses the same panels. All choices are read
// from and written to ProfileStore.Current, which persists across restarts.
public class MainMenu : MonoBehaviour
{
    private enum Panel { None, Skills, Gear, Appearance, Options, Summon }

    // Live character preview shown on the Appearance panel - see
    // CharacterPreview. Both are optional (null until the Editor-side
    // preview stage is set up); the panel falls back to a placeholder
    // message rather than failing when either isn't wired yet.
    [SerializeField] private CharacterPreview characterPreview;
    [SerializeField] private Texture previewRenderTexture;

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
    private int appearanceTabIndex;
    private Vector2 appearanceTabScrollPosition;
    private Vector2 summonScrollPosition;
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
            case Panel.Appearance:
                DrawAppearancePanel();
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

        if (ability.IsAuraSpell)
        {
            sb.AppendLine("Always active once slotted - no cast, no keybind, no mana cost.");
            if (ability.Effect != null) sb.AppendLine(DescribeEffect(ability.Effect, ability.Effect.Duration));
            if ((ability.AuraReveals & MinimapReveal.Mobs) != 0) sb.AppendLine("Reveals every mob on your minimap.");
            return sb.ToString().TrimEnd();
        }

        if (ability.Damage > 0f) sb.AppendLine($"Damage: {ability.Damage}");
        if (ability.HealAmount > 0f) sb.AppendLine($"Heals: {ability.HealAmount}");
        if (ability.ShieldAmount > 0f) sb.AppendLine($"Shields for: {ability.ShieldAmount}");
        sb.AppendLine($"Mana Cost: {ability.ManaCost}");
        sb.AppendLine($"Cooldown: {ability.Cooldown}s");
        if (ability.CastTime > 0f) sb.AppendLine($"Cast Time: {ability.CastTime}s");
        if (!ability.AreaAroundCaster) sb.AppendLine($"Range: {ability.Range}");
        if (ability.IsGroundTargeted)
        {
            sb.AppendLine($"Ground-targeted: {ability.GroundEffectRadius * 2f} diameter area");
            if (ability.ForceSpeed > 0f)
            {
                string direction = ability.PushAway ? "outward, away from" : "inward, toward";
                sb.AppendLine($"Blasts everyone in the area {direction} the center at {ability.ForceSpeed}/s");
            }
        }
        if (ability.AreaAroundCaster) sb.AppendLine($"Centered on you: {ability.GroundEffectRadius} radius, affects all players");
        if (ability.RecallTarget) sb.AppendLine("Teleports the target to your location");

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
        if (effect.TickHeal > 0f) sb.Append($" +{effect.TickHeal} hp/{effect.TickInterval}s");
        if (effect.DamageRedirectPercent > 0f) sb.Append($" redirects {effect.DamageRedirectPercent * 100f:0}% of damage taken to the caster");
        foreach (StatBonus bonus in effect.Modifiers) sb.Append(' ').Append(DescribeBonus(bonus));
        sb.Append($" for {duration}s");
        return sb.ToString();
    }

    private static string BuildItemTooltip(ItemData item)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine(item.ItemName);
        sb.AppendLine($"Slot: {item.Slot}");
        if (item.Weapon != null) sb.AppendLine($"Weapon: {item.Weapon.Damage} dmg every {item.Weapon.SwingInterval}s");

        foreach (StatBonus bonus in item.Bonuses) sb.AppendLine(DescribeBonus(bonus));
        foreach (EffectImmunity immunity in item.Immunities)
        {
            if (immunity.Effect == null) continue;
            sb.AppendLine($"Immune to {immunity.Effect.DisplayName}{(immunity.GroundOnly ? " from ground effects" : "")}");
        }
        foreach (ItemAura aura in item.Auras)
        {
            if (aura.Effect == null) continue;
            sb.Append(aura.Range > 0f ? $"Aura ({aura.Range} range): " : "While worn: ").Append(aura.Effect.DisplayName);
            if (aura.Effect.TickDamage > 0f) sb.Append($", {aura.Effect.TickDamage} dmg/{aura.Effect.TickInterval}s");
            if (aura.Effect.TickHeal > 0f) sb.Append($", +{aura.Effect.TickHeal} hp/{aura.Effect.TickInterval}s");
            foreach (StatBonus bonus in aura.Effect.Modifiers) sb.Append(", ").Append(DescribeBonus(bonus));
            sb.AppendLine();
        }
        if ((item.Reveals & MinimapReveal.Players) != 0) sb.AppendLine("Reveals players on the minimap");
        if ((item.Reveals & MinimapReveal.Mobs) != 0) sb.AppendLine("Pulses monsters onto the minimap every 5s");
        if (item.BroadcastsLocation) sb.AppendLine("Broadcasts your location to allies' minimaps");

        return sb.ToString().TrimEnd();
    }

    private static string DescribeBonus(StatBonus bonus)
    {
        bool isPercent = bonus.ModifierType == StatModifierType.PercentAdditive;
        float displayValue = isPercent ? bonus.Value * 100f : bonus.Value;
        if (bonus.Stat == StatType.ManaCostMultiplier)
        {
            return $"Mana cost {displayValue:+0.#;-0.#}{(isPercent ? "%" : "")}";
        }
        if (bonus.Stat == StatType.ThreatMultiplier)
        {
            return $"Threat generated {displayValue:+0.#;-0.#}{(isPercent ? "%" : "")}";
        }
        if (bonus.Stat == StatType.DamageMultiplier)
        {
            return $"Damage dealt {displayValue:+0.#;-0.#}{(isPercent ? "%" : "")}";
        }
        if (bonus.Stat == StatType.HealingMultiplier)
        {
            return $"Healing done {displayValue:+0.#;-0.#}{(isPercent ? "%" : "")}";
        }
        string sign = displayValue >= 0f ? "+" : "";
        return $"{sign}{displayValue}{(isPercent ? "%" : "")} {bonus.Stat}";
    }

    private void OpenPanel(Panel panel)
    {
        activePanel = panel;
        scrollPosition = Vector2.zero;
        appearanceTabIndex = 0;
        appearanceTabScrollPosition = Vector2.zero;
    }

    // Buttons grouped by purpose rather than a flat list - character-build
    // choices together, then utility tools, then the action that leaves the
    // menu - per the game-ui-design skill's guidance for this surface (a
    // low-stakes flat menu: group for scannability, don't over-design it).
    private void DrawMainPanel()
    {
        GUILayout.BeginArea(new Rect(UIScale.Width / 2f - 100, UIScale.Height / 2f - 140, 200, 280));
        if (GUILayout.Button("Choose Skills", GUILayout.Height(40))) OpenPanel(Panel.Skills);
        if (GUILayout.Button("Choose Gear", GUILayout.Height(40))) OpenPanel(Panel.Gear);
        if (GUILayout.Button("Character Creation", GUILayout.Height(40))) OpenPanel(Panel.Appearance);
        GUILayout.Space(10);
        if (GUILayout.Button("Options", GUILayout.Height(40))) OpenPanel(Panel.Options);
        GUILayout.Space(10);
        if (GUILayout.Button("Enter Testing Area", GUILayout.Height(40)))
        {
            ProfileStore.Save();
            TestingAreaGate.Entered = true;
        }
        GUILayout.EndArea();
    }

    private void DrawInGamePanel()
    {
        GUILayout.BeginArea(new Rect(UIScale.Width / 2f - 100, UIScale.Height / 2f - 160, 200, 320));
        if (GUILayout.Button("Skills", GUILayout.Height(40))) OpenPanel(Panel.Skills);
        if (GUILayout.Button("Gear", GUILayout.Height(40))) OpenPanel(Panel.Gear);
        if (GUILayout.Button("Character Creation", GUILayout.Height(40))) OpenPanel(Panel.Appearance);
        GUILayout.Space(10);
        if (GUILayout.Button("Summon Mobs", GUILayout.Height(40))) OpenPanel(Panel.Summon);
        if (GUILayout.Button("Options", GUILayout.Height(40))) OpenPanel(Panel.Options);
        GUILayout.Space(10);
        if (GUILayout.Button("Resume", GUILayout.Height(40))) CloseInGameMenu();
        GUILayout.EndArea();
    }

    // Testing-lobby tool: spawn mobs to fight. Buttons rather than a text
    // field for the count, because IMGUI's native Tab focus traversal grabs
    // any focusable control and Tab is the tab-targeting key.
    private void DrawSummonPanel()
    {
        GUILayout.BeginArea(new Rect(UIScale.Width / 2f - 120, UIScale.Height / 2f - 220, 240, 440));
        GUILayout.Label("Summon Mobs");

        PlayerSummon summon = LocalPlayer<PlayerSummon>();
        if (summon == null || summon.SummonableMobs.Count == 0)
        {
            GUILayout.Label("No player spawned yet.");
        }
        else
        {
            // The mob-type list scrolls independently so it can keep
            // growing (e.g. the Skeleton Tactician escort) without pushing
            // the count controls / Summon / Back buttons below the fixed
            // area and out of view - GUILayout.BeginArea clips silently
            // instead of scrolling on its own.
            summonScrollPosition = GUILayout.BeginScrollView(summonScrollPosition, GUILayout.Height(220));
            for (int i = 0; i < summon.SummonableMobs.Count; i++)
            {
                string label = (i == summonMobIndex ? "> " : "") + PlayerSummon.MobLabel(summon.SummonableMobs[i]);
                if (GUILayout.Button(label)) summonMobIndex = i;
            }
            GUILayout.EndScrollView();

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

            GUILayout.Space(10);
            if (GUILayout.Button("Summon Skeletons", GUILayout.Height(30)))
            {
                summon.RequestSummonSkeletonEncounter();
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
            string keyLabel = slotAbility != null && slotAbility.IsAuraSpell
                ? "Always On"
                : (slotKey.HasValue ? slotKey.Value.DisplayName : "Unbound");
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
                if (!slotAbility.IsAuraSpell
                    && GUILayout.Button(awaitingKeyForSlot == i ? "Press a key..." : "Set Key Binding"))
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

    // Indexed by GearSlot.
    private static readonly string[] SlotShortNames =
    {
        "Head", "Neck", "Chest", "Cape", "Gloves", "Legs", "Boots",
        "Ring 1", "Ring 2", "Trinket", "Main", "Off",
    };

    // Slot-category outline colors for the gear grid (both the equipment
    // paper-doll and the inventory side, keyed by whichever slot an item
    // actually belongs to - GearSlotExtensions.IsRing() means either ring
    // slot maps the same way). Slots not listed here (Helmet, Cape,
    // Gloves, Legs) get no colored outline.
    private static Color? GetSlotOutlineColor(GearSlot slot)
    {
        if (slot.IsRing()) return Color.red;
        switch (slot)
        {
            case GearSlot.Trinket: return Color.green;
            case GearSlot.MainHand: return Color.cyan;
            case GearSlot.OffHand: return new Color(0.6f, 0f, 1f);
            case GearSlot.Necklace: return Color.yellow;
            case GearSlot.Chest: return Color.blue;
            case GearSlot.Boots: return Color.black;
            default: return null;
        }
    }

    // Drawn just before the slot's button, slightly larger than it, so the
    // button's own opaque background covers the middle and only a border a
    // little bolder than the default button bevel shows around the edge.
    private static void DrawSlotOutline(Rect rect, GearSlot slot)
    {
        Color? color = GetSlotOutlineColor(slot);
        if (!color.HasValue) return;

        const float thickness = 2f;
        Color previous = GUI.color;
        GUI.color = color.Value;
        GUI.DrawTexture(new Rect(rect.x - thickness, rect.y - thickness, rect.width + thickness * 2f, rect.height + thickness * 2f), Texture2D.whiteTexture);
        GUI.color = previous;
    }

    // Paper-doll on the left, inventory grid on the right. Items draw as an
    // "X" placeholder until there's 2D art; hovering tells you what it is.
    // "Inventory" is every item in the game that isn't equipped - there's
    // no real inventory/loot system yet.
    private void DrawGearPanel()
    {
        const float cell = 60f;
        const float gap = 6f;
        const float inventoryX = 240f;
        const int inventoryColumns = 6;

        GUILayout.BeginArea(new Rect(UIScale.Width / 2f - 320, UIScale.Height / 2f - 220, 640, 440));
        GUI.Label(new Rect(0, 0, 200, 20), "Equipment");
        GUI.Label(new Rect(inventoryX, 0, 200, 20), "Inventory");

        GUIStyle icon = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter, fontSize = 22, fontStyle = FontStyle.Bold };
        GUIStyle tag = new GUIStyle(GUI.skin.label) { fontSize = 9, alignment = TextAnchor.UpperLeft };

        GearSlot[] slots = (GearSlot[])System.Enum.GetValues(typeof(GearSlot));
        for (int i = 0; i < slots.Length; i++)
        {
            Rect rect = new Rect((i % 3) * (cell + gap), 24f + (i / 3) * (cell + gap), cell, cell);
            ItemData equipped = Profile.GetGear(slots[i]);
            string tooltip = equipped != null ? BuildItemTooltip(equipped) + "\n(click to unequip)" : $"{SlotShortNames[i]} (empty)";

            DrawSlotOutline(rect, slots[i]);
            if (GUI.Button(rect, new GUIContent(equipped != null ? "X" : "", tooltip), icon) && equipped != null)
            {
                Profile.SetGear(slots[i], null);
            }
            GUI.Label(new Rect(rect.x + 3f, rect.y + 2f, cell - 6f, 14f), SlotShortNames[i], tag);
        }

        int index = 0;
        foreach (ItemData item in GameDatabase.Items)
        {
            if (Profile.IsEquipped(item)) continue;

            Rect rect = new Rect(inventoryX + (index % inventoryColumns) * (cell + gap), 24f + (index / inventoryColumns) * (cell + gap), cell, cell);
            index++;

            DrawSlotOutline(rect, item.Slot);
            if (GUI.Button(rect, new GUIContent("X", BuildItemTooltip(item) + "\n(click to equip)"), icon))
            {
                GearSlot targetSlot = TargetSlotFor(item);

                // A two-handed weapon and an off-hand item can't coexist -
                // equipping either one auto-clears whichever conflicts with
                // it, mirroring CharacterEquipment.SetGearServerRpc's
                // authoritative rule (so the server never has to silently
                // reject what this menu just showed as equipped).
                if (targetSlot == GearSlot.MainHand && item.TwoHanded)
                {
                    Profile.SetGear(GearSlot.OffHand, null);
                }
                else if (targetSlot == GearSlot.OffHand)
                {
                    ItemData mainHand = Profile.GetGear(GearSlot.MainHand);
                    if (mainHand != null && mainHand.TwoHanded) Profile.SetGear(GearSlot.MainHand, null);
                }

                Profile.SetGear(targetSlot, item);
            }
        }
        if (index == 0) GUI.Label(new Rect(inventoryX, 24f, 300f, 20f), "(nothing left to equip)");

        if (GUI.Button(new Rect(0, 410f, 100f, 26f), "Back")) LeavePanel();
        GUILayout.EndArea();
    }

    // Rings equip into whichever physical ring slot is free (Ring1 first);
    // if both are occupied, Ring1 is overwritten, same as any other slot.
    private static GearSlot TargetSlotFor(ItemData item)
    {
        if (!item.Slot.IsRing()) return item.Slot;
        if (Profile.GetGear(GearSlot.Ring1) == null) return GearSlot.Ring1;
        if (Profile.GetGear(GearSlot.Ring2) == null) return GearSlot.Ring2;
        return GearSlot.Ring1;
    }

    // Cosmetic-only: everything except weapons is selectable, independently
    // per category (not locked to the pack's pre-built class presets), plus
    // two recolor swatches. No stat effect - see CharacterAppearance.
    // Weapons are never shown here (see the character creation plan notes -
    // a visually-equipped weapon is planned as a future Gear-driven feature
    // instead). A live preview (CharacterPreview, rendered into
    // previewRenderTexture) sits alongside the choices so a pick's effect is
    // visible immediately. With nine selectable categories, a flat row of
    // parallel columns (the original Top/Bottom/Headwear layout) doesn't
    // scale - a category-tab list replaces it, one list/checklist visible
    // at a time, per the game-ui-design skill's guidance to group by
    // purpose rather than cram everything into view at once.
    private static readonly string[] AppearanceTabLabels =
    {
        "Top", "Bottom", "Headwear", "Hair", "Facial Hair", "Eyebrows", "Eyes", "Mouth", "Accessories"
    };

    private void DrawAppearancePanel()
    {
        const float previewWidth = 200f;
        const float previewHeight = 380f;
        const float contentX = previewWidth + 30f;
        const float tabColumnWidth = 130f;
        const float listHeight = 320f;
        const float panelWidth = 700f;
        const float panelHeight = 520f;

        float panelX = UIScale.Width / 2f - panelWidth / 2f;
        float panelY = UIScale.Height / 2f - panelHeight / 2f;

        GUI.Label(new Rect(panelX, panelY, 400, 24), "Character Creation");

        Rect previewRect = new Rect(panelX, panelY + 30f, previewWidth, previewHeight);
        GUI.Box(previewRect, GUIContent.none);
        if (characterPreview != null) characterPreview.Refresh(Profile);
        if (previewRenderTexture != null)
        {
            GUI.DrawTexture(previewRect, previewRenderTexture, ScaleMode.ScaleToFit);
        }
        else
        {
            GUIStyle centeredWrap = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            GUI.Label(previewRect, "Preview not configured yet", centeredWrap);
        }

        // Hold to spin the preview character to see it from other angles -
        // see CharacterPreview.Rotate. GUI.RepeatButton (not GUI.Button) so
        // holding the mouse down keeps rotating smoothly instead of one
        // fixed step per click.
        float rotateY = previewRect.yMax + 6f;
        Rect rotateLeftRect = new Rect(previewRect.x, rotateY, previewWidth / 2f - 3f, 28f);
        Rect rotateRightRect = new Rect(previewRect.x + previewWidth / 2f + 3f, rotateY, previewWidth / 2f - 3f, 28f);
        if (characterPreview != null)
        {
            if (GUI.RepeatButton(rotateLeftRect, "< Rotate")) characterPreview.Rotate(-90f * Time.deltaTime);
            if (GUI.RepeatButton(rotateRightRect, "Rotate >")) characterPreview.Rotate(90f * Time.deltaTime);
        }

        // +30f matches previewRect's own header clearance above, so the
        // Gender row doesn't draw on top of the "Character Creation" title.
        GUILayout.BeginArea(new Rect(panelX + contentX, panelY + 30f, panelWidth - contentX, panelHeight - 30f));

        GUILayout.BeginHorizontal();
        GUILayout.Label("Gender:", GUILayout.Width(60));
        string maleLabel = (!Profile.AppearanceIsFemale ? "> " : "") + "Male";
        if (GUILayout.Button(maleLabel, GUILayout.Width(90)) && Profile.AppearanceIsFemale)
        {
            Profile.AppearanceIsFemale = false;
            Profile.Normalize();
        }
        string femaleLabel = (Profile.AppearanceIsFemale ? "> " : "") + "Female";
        if (GUILayout.Button(femaleLabel, GUILayout.Width(90)) && !Profile.AppearanceIsFemale)
        {
            Profile.AppearanceIsFemale = true;
            Profile.Normalize();
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(6);

        AppearanceGender gender = Profile.Gender;

        GUILayout.BeginHorizontal();

        GUILayout.BeginVertical(GUILayout.Width(tabColumnWidth));
        for (int i = 0; i < AppearanceTabLabels.Length; i++)
        {
            string tabLabel = (i == appearanceTabIndex ? "> " : "") + AppearanceTabLabels[i];
            if (GUILayout.Button(tabLabel))
            {
                appearanceTabIndex = i;
                appearanceTabScrollPosition = Vector2.zero;
            }
        }
        GUILayout.EndVertical();

        GUILayout.BeginVertical();
        DrawAppearanceTabContent(gender, listHeight);
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
        GUILayout.Space(10);

        AppearanceColorPalette palette = GameDatabase.Palette;
        int bodyCount = palette != null && palette.BodyColors != null ? palette.BodyColors.Length : 0;
        int objectCount = palette != null && palette.ObjectColors != null ? palette.ObjectColors.Length : 0;

        DrawColorStepper("Body Color", ref Profile.AppearanceBodyColorIndex, bodyCount);
        DrawColorStepper("Gear Color", ref Profile.AppearanceObjectColorIndex, objectCount);

        GUILayout.Space(10);
        if (GUILayout.Button("Back")) LeavePanel();
        GUILayout.EndArea();
    }

    // Picking a Bottom that was designed to be worn with a specific
    // accessory (e.g. Sorcerer's leaves the shins bare without its matching
    // Shoes) auto-enables that accessory the moment it's picked - a helpful
    // default, not a forced-on rule, so the player can still turn it back
    // off afterward in the Accessories tab if they want.
    private static void SetBottom(string id)
    {
        Profile.AppearanceBottomId = id;
        AppearancePieceData piece = GameDatabase.GetAppearancePiece(id);
        if (piece != null && !string.IsNullOrEmpty(piece.RequiredAccessoryId))
        {
            Profile.ToggleAccessory(piece.RequiredAccessoryId, true);
        }
    }

    private void DrawAppearanceTabContent(AppearanceGender gender, float listHeight)
    {
        switch (appearanceTabIndex)
        {
            case 0:
                DrawSelectableList("Top", ref appearanceTabScrollPosition, listHeight, Profile.AppearanceTopId, id => Profile.AppearanceTopId = id,
                    GameDatabase.AppearancePieces.Where(p => p.Slot == AppearanceSlot.Top && p.Gender == gender).Select(p => (p.Id, p.DisplayName)));
                break;
            case 1:
                DrawSelectableList("Bottom", ref appearanceTabScrollPosition, listHeight, Profile.AppearanceBottomId, SetBottom,
                    GameDatabase.AppearancePieces.Where(p => p.Slot == AppearanceSlot.Bottom && p.Gender == gender).Select(p => (p.Id, p.DisplayName)));
                break;
            case 2:
                IEnumerable<(string Id, string DisplayName)> headwearOptions = new[] { ("", "None") }
                    .Concat(GameDatabase.AppearanceHeadwear.Where(h => h.Gender == AppearanceGender.Unisex || h.Gender == gender).Select(h => (h.Id, h.DisplayName)));
                DrawSelectableList("Headwear", ref appearanceTabScrollPosition, listHeight, Profile.AppearanceHeadwearId, id => Profile.AppearanceHeadwearId = id, headwearOptions);
                break;
            case 3:
                IEnumerable<(string Id, string DisplayName)> hairOptions = new[] { ("", "None") }
                    .Concat(GameDatabase.AppearancePieces.Where(p => p.Slot == AppearanceSlot.Hair && p.Gender == gender).Select(p => (p.Id, p.DisplayName)));
                DrawSelectableList("Hair", ref appearanceTabScrollPosition, listHeight, Profile.AppearanceHairId, id => Profile.AppearanceHairId = id, hairOptions);
                break;
            case 4:
                IEnumerable<(string Id, string DisplayName)> facialHairOptions = new[] { ("", "None") }
                    .Concat(GameDatabase.AppearancePieces.Where(p => p.Slot == AppearanceSlot.FacialHair && p.Gender == gender).Select(p => (p.Id, p.DisplayName)));
                DrawSelectableList("Facial Hair", ref appearanceTabScrollPosition, listHeight, Profile.AppearanceFacialHairId, id => Profile.AppearanceFacialHairId = id, facialHairOptions);
                break;
            case 5:
                DrawSelectableList("Eyebrows", ref appearanceTabScrollPosition, listHeight, Profile.AppearanceEyebrowsId, id => Profile.AppearanceEyebrowsId = id,
                    GameDatabase.AppearancePieces.Where(p => p.Slot == AppearanceSlot.Eyebrows && p.Gender == gender).Select(p => (p.Id, p.DisplayName)));
                break;
            case 6:
                DrawSelectableList("Eyes", ref appearanceTabScrollPosition, listHeight, Profile.AppearanceEyesId, id => Profile.AppearanceEyesId = id,
                    GameDatabase.AppearancePieces.Where(p => p.Slot == AppearanceSlot.Eyes && p.Gender == gender).Select(p => (p.Id, p.DisplayName)));
                break;
            case 7:
                DrawSelectableList("Mouth", ref appearanceTabScrollPosition, listHeight, Profile.AppearanceMouthId, id => Profile.AppearanceMouthId = id,
                    GameDatabase.AppearancePieces.Where(p => p.Slot == AppearanceSlot.Mouth && p.Gender == gender).Select(p => (p.Id, p.DisplayName)));
                break;
            case 8:
                GUILayout.Label("Accessories (any combination)");
                DrawSelectableChecklist(ref appearanceTabScrollPosition, listHeight,
                    GameDatabase.AppearanceAccessories.Where(a => a.Gender == gender).Select(a => (a.Id, a.DisplayName)),
                    Profile.HasAccessory, Profile.ToggleAccessory);
                break;
        }
    }

    // Shared by every single-select tab of DrawAppearancePanel - identical
    // scroll-list-of-selectable-buttons shape, differing only in which
    // options are offered and which Profile field the selection reads/writes.
    private static void DrawSelectableList(string label, ref Vector2 scrollPosition, float height, string currentId,
        System.Action<string> setId, IEnumerable<(string Id, string DisplayName)> options)
    {
        GUILayout.Label(label);
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(height));
        foreach ((string id, string displayName) in options)
        {
            string buttonLabel = (id == currentId ? "> " : "") + displayName;
            if (GUILayout.Button(buttonLabel)) setId(id);
        }
        GUILayout.EndScrollView();
    }

    // The Accessories tab's free multi-select - same shape as
    // DrawSelectableList but every option toggles independently rather than
    // exactly one being current.
    private static void DrawSelectableChecklist(ref Vector2 scrollPosition, float height,
        IEnumerable<(string Id, string DisplayName)> options, System.Func<string, bool> isSelected, System.Action<string, bool> setSelected)
    {
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(height));
        foreach ((string id, string displayName) in options)
        {
            bool current = isSelected(id);
            bool next = GUILayout.Toggle(current, displayName);
            if (next != current) setSelected(id, next);
        }
        GUILayout.EndScrollView();
    }

    // Shared by the Body/Gear color rows of DrawAppearancePanel.
    private static void DrawColorStepper(string label, ref int index, int count)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label} ({(count > 0 ? index + 1 : 0)}/{count})", GUILayout.Width(160));
        if (GUILayout.Button("<", GUILayout.Width(30)) && count > 0) index = (index - 1 + count) % count;
        if (GUILayout.Button(">", GUILayout.Width(30)) && count > 0) index = (index + 1) % count;
        GUILayout.EndHorizontal();
    }

    private void DrawOptionsPanel()
    {
        GUILayout.BeginArea(new Rect(UIScale.Width / 2f - 150, UIScale.Height / 2f - 170, 300, 340));

        GUILayout.Label($"UI Scale ({UIScale.Value * 100f:0}%)");
        UIScale.Value = GUILayout.HorizontalSlider(UIScale.Value, UIScale.Min, UIScale.Max);

        GUILayout.Space(10);
        GUILayout.Label($"Look Sensitivity ({LookSensitivityScale.Value:0.00}x)");
        LookSensitivityScale.Value = GUILayout.HorizontalSlider(LookSensitivityScale.Value, LookSensitivityScale.Min, LookSensitivityScale.Max);

        GUILayout.Space(10);
        GUILayout.Label("Keybindings");

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
