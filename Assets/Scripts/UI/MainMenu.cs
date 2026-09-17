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

    // UI Toolkit panels (see CLAUDE.md's Menus/dev UI section), each
    // replacing an old IMGUI Draw*Panel method entirely. Null-guarded like
    // characterPreview: if not yet wired in the Editor, that one panel
    // simply won't open (no error), rather than being a hard requirement to
    // compile/run.
    [SerializeField] private OptionsPanelController optionsPanelUI;
    [SerializeField] private MenuShellController menuShellUI;
    [SerializeField] private SummonPanelController summonPanelUI;
    [SerializeField] private SkillsPanelController skillsPanelUI;

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
    private int summonMobIndex;
    private int summonCount = 1;
    private PlayerSummon summonListBuiltFor;

    private static PlayerProfile Profile => ProfileStore.Current;

    private void Awake()
    {
        if (optionsPanelUI != null)
        {
            optionsPanelUI.BackRequested += LeavePanel;
            optionsPanelUI.ResetRequested += ResetKeybindingsToDefaults;
            optionsPanelUI.KeyRowClicked += BeginCaptureMovementKey;
            optionsPanelUI.UiScaleChanged += value => UIScale.Value = value;
            optionsPanelUI.LookSensitivityChanged += value => LookSensitivityScale.Value = value;
        }
        else
        {
            Debug.LogWarning("MainMenu: Options Panel Ui isn't assigned - the Options panel won't open.");
        }

        if (menuShellUI != null)
        {
            menuShellUI.SkillsClicked += () => OpenPanel(Panel.Skills);
            menuShellUI.GearClicked += () => OpenPanel(Panel.Gear);
            menuShellUI.AppearanceClicked += () => OpenPanel(Panel.Appearance);
            menuShellUI.SummonClicked += () => OpenPanel(Panel.Summon);
            menuShellUI.OptionsClicked += () => OpenPanel(Panel.Options);
            menuShellUI.RespawnClicked += () => LocalPlayer<CharacterStats>()?.RequestRespawn();
            menuShellUI.EnterTestingAreaClicked += EnterTestingArea;
            menuShellUI.ResumeClicked += CloseInGameMenu;
        }
        else
        {
            Debug.LogWarning("MainMenu: Menu Shell Ui isn't assigned - the main/in-game menu won't open.");
        }

        if (summonPanelUI != null)
        {
            summonPanelUI.BackRequested += LeavePanel;
            summonPanelUI.MobRowClicked += SelectSummonMob;
            summonPanelUI.CountDecreaseRequested += DecreaseSummonCount;
            summonPanelUI.CountIncreaseRequested += IncreaseSummonCount;
            summonPanelUI.SummonRequested += RequestSummon;
            summonPanelUI.SummonSkeletonsRequested += RequestSummonSkeletons;
        }
        else
        {
            Debug.LogWarning("MainMenu: Summon Panel Ui isn't assigned - the Summon Mobs panel won't open.");
        }

        if (skillsPanelUI != null)
        {
            skillsPanelUI.BackRequested += LeavePanel;
            skillsPanelUI.AvailableAbilityClicked += AssignAbilityToFirstEmptySlot;
            skillsPanelUI.SlotClicked += ToggleSelectedSkillSlot;
            skillsPanelUI.RemoveClicked += RemoveSkillSlot;
            skillsPanelUI.SetKeyBindingClicked += BeginCaptureAbilityKey;
        }
        else
        {
            Debug.LogWarning("MainMenu: Skills Panel Ui isn't assigned - the Skills panel won't open.");
        }
    }

    private void Start()
    {
        // Deferred from Awake(): UIDocument builds its rootVisualElement in
        // its own OnEnable, which runs after every object's Awake in the
        // scene, so touching it any earlier would hit a null root. Start()
        // runs after every OnEnable has completed, so this is always safe.
        optionsPanelUI?.BuildRows(MovementActionNames);
        RefreshPanelVisibility();
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
            // Capture just completed this frame - the affected slot's (and
            // possibly another slot's, if a key got stolen) label changed,
            // so the already-built rows need rebuilding. Old IMGUI just
            // recomputed every label fresh every frame; this only redoes it
            // on the one frame something actually changed.
            if (awaitingKeyForSlot < 0) RebuildSkillsLists();
        }
        else if (awaitingKeyForMovement >= 0)
        {
            CaptureMovementKey();
        }

        if (optionsPanelUI != null && activePanel == Panel.Options)
        {
            optionsPanelUI.Refresh(Profile.MovementKeys, awaitingKeyForMovement, UIScale.Value, LookSensitivityScale.Value);
        }

        if (summonPanelUI != null && activePanel == Panel.Summon)
        {
            // The local player (and PlayerSummon.SummonableMobs) may not
            // exist yet the instant the panel opens - old IMGUI just re-read
            // it fresh every OnGUI call, so this re-checks once per distinct
            // player instance instead of building the row list only once
            // in OpenPanel(), which could otherwise leave the list
            // permanently empty if it opened too early.
            PlayerSummon summon = LocalPlayer<PlayerSummon>();
            if (summon != null && summon != summonListBuiltFor)
            {
                summonPanelUI.SetMobList(BuildSummonMobLabels());
                summonListBuiltFor = summon;
            }
            summonPanelUI.Refresh(summon != null && summon.SummonableMobs.Count > 0, summonMobIndex, summonCount);
        }

        if (skillsPanelUI != null && activePanel == Panel.Skills)
        {
            skillsPanelUI.RefreshSelection(selectedSlot, awaitingKeyForSlot);
        }
    }

    private void BeginCaptureMovementKey(int index)
    {
        awaitingKeyForMovement = awaitingKeyForMovement == index ? -1 : index;
        awaitingKeyForSlot = -1;
    }

    private void ResetKeybindingsToDefaults()
    {
        System.Array.Copy(MovementInput.Defaults, Profile.MovementKeys, Profile.MovementKeys.Length);
        awaitingKeyForMovement = -1;
    }

    private void EnterTestingArea()
    {
        ProfileStore.Save();
        TestingAreaGate.Entered = true;
        RefreshPanelVisibility();
    }

    private void SelectSummonMob(int index)
    {
        summonMobIndex = index;
    }

    private void DecreaseSummonCount()
    {
        summonCount = Mathf.Max(1, summonCount - 1);
    }

    private void IncreaseSummonCount()
    {
        PlayerSummon summon = LocalPlayer<PlayerSummon>();
        int max = summon != null ? summon.MaxSummonCount : summonCount;
        summonCount = Mathf.Min(max, summonCount + 1);
    }

    private void RequestSummon()
    {
        PlayerSummon summon = LocalPlayer<PlayerSummon>();
        if (summon == null) return;
        summon.RequestSummon(summonMobIndex, summonCount);
        CloseInGameMenu();
    }

    private void RequestSummonSkeletons()
    {
        PlayerSummon summon = LocalPlayer<PlayerSummon>();
        if (summon == null) return;
        summon.RequestSummonSkeletonEncounter();
        CloseInGameMenu();
    }

    private List<string> BuildSummonMobLabels()
    {
        List<string> labels = new List<string>();
        PlayerSummon summon = LocalPlayer<PlayerSummon>();
        if (summon == null) return labels;
        foreach (var mob in summon.SummonableMobs) labels.Add(PlayerSummon.MobLabel(mob));
        return labels;
    }

    private void AssignAbilityToFirstEmptySlot(AbilityData ability)
    {
        int emptySlot = System.Array.IndexOf(Profile.SlotAbilityIds, null);
        if (emptySlot < 0) emptySlot = System.Array.IndexOf(Profile.SlotAbilityIds, "");
        if (emptySlot >= 0) Profile.SetSlotAbility(emptySlot, ability);
        RebuildSkillsLists();
    }

    private void ToggleSelectedSkillSlot(int index)
    {
        selectedSlot = selectedSlot == index ? -1 : index;
        awaitingKeyForSlot = -1;
    }

    private void RemoveSkillSlot(int index)
    {
        Profile.SetSlotAbility(index, null);
        Profile.SetSlotKey(index, null);
        selectedSlot = -1;
        // Also true of the old IMGUI Remove handler - if a key capture was
        // armed for this same slot (reachable: Remove and Set Key Binding
        // sit in the same expanded sub-row), leaving awaitingKeyForSlot
        // pointing at the now-empty slot means the next keypress binds a
        // key to nothing and silently steals it from whichever slot
        // actually owned it.
        awaitingKeyForSlot = -1;
        RebuildSkillsLists();
        // RebuildLists() rebuilds the toolbar but doesn't touch the shared
        // sub-row - without this, it would keep showing the just-removed
        // ability's stale name/buttons until the next Update() tick.
        skillsPanelUI?.RefreshSelection(selectedSlot, awaitingKeyForSlot);
    }

    private void BeginCaptureAbilityKey(int index)
    {
        awaitingKeyForSlot = awaitingKeyForSlot == index ? -1 : index;
    }

    // Fixed order/names, per the user's explicit categorization. Every check
    // reads a field that already exists on AbilityData - nothing invented.
    // Checked top to bottom, first match wins (e.g. a melee ability that
    // also deals Damage lands in Melee, not Damage Spells).
    private static readonly string[] AbilityCategoryNames = { "AURAS", "HEALS", "MELEE", "DAMAGE SPELLS", "UTILITY" };

    private static int CategorizeAbility(AbilityData ability)
    {
        if (ability.IsAuraSpell) return 0;
        if (ability.HealAmount > 0f || ability.ShieldAmount > 0f) return 1;
        if (ability.RequiresMeleeWeapon) return 2;
        if (ability.Damage > 0f) return 3;
        return 4;
    }

    private void RebuildSkillsLists()
    {
        if (skillsPanelUI == null) return;

        List<AbilityCategoryDisplay> categories = new List<AbilityCategoryDisplay>();
        foreach (string name in AbilityCategoryNames)
        {
            categories.Add(new AbilityCategoryDisplay { CategoryName = name, Abilities = new List<AbilityData>() });
        }

        foreach (AbilityData ability in GameDatabase.Abilities)
        {
            if (Profile.IndexOfAbility(ability) < 0) categories[CategorizeAbility(ability)].Abilities.Add(ability);
        }

        List<SkillSlotDisplay> slots = new List<SkillSlotDisplay>();
        for (int i = 0; i < PlayerProfile.AbilitySlots; i++)
        {
            AbilityData slotAbility = Profile.GetSlotAbility(i);
            if (slotAbility == null)
            {
                slots.Add(new SkillSlotDisplay { Ability = null, KeyLabel = "", CanSetKeyBinding = false });
                continue;
            }

            KeyBindingOption? slotKey = Profile.GetSlotKey(i);
            string keyLabel = slotAbility.IsAuraSpell
                ? "Always On"
                : (slotKey.HasValue ? slotKey.Value.DisplayName : "Unbound");
            slots.Add(new SkillSlotDisplay
            {
                Ability = slotAbility,
                KeyLabel = keyLabel,
                CanSetKeyBinding = !slotAbility.IsAuraSpell,
            });
        }

        skillsPanelUI.RebuildLists(categories, slots);
    }

    // Centralizes which UI Toolkit panel (if any) is visible for the
    // current activePanel/IsOpen/TestingAreaGate.Entered combination -
    // called from every panel-state transition instead of each transition
    // method managing its own Show()/Hide() calls, so a future panel only
    // needs one more line here rather than touching every transition method.
    private void RefreshPanelVisibility()
    {
        bool showShell = activePanel == Panel.None && (!TestingAreaGate.Entered || IsOpen);
        if (showShell)
        {
            menuShellUI?.SetMode(TestingAreaGate.Entered);
            menuShellUI?.Show();
        }
        else
        {
            menuShellUI?.Hide();
        }

        if (activePanel == Panel.Options) optionsPanelUI?.Show(); else optionsPanelUI?.Hide();
        if (activePanel == Panel.Summon) summonPanelUI?.Show(); else summonPanelUI?.Hide();
        if (activePanel == Panel.Skills) skillsPanelUI?.Show(); else skillsPanelUI?.Hide();
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
        RefreshPanelVisibility();
    }

    private void CloseInGameMenu()
    {
        IsOpen = false;
        activePanel = Panel.None;
        selectedSlot = -1;
        awaitingKeyForSlot = -1;
        awaitingKeyForMovement = -1;
        RefreshPanelVisibility();
        ProfileStore.Save();
        Closed?.Invoke();
    }

    private void LeavePanel()
    {
        activePanel = Panel.None;
        selectedSlot = -1;
        awaitingKeyForSlot = -1;
        awaitingKeyForMovement = -1;
        RefreshPanelVisibility();
        ProfileStore.Save();
    }

    // Any non-mouse key is allowed, same pool CaptureMovementKey already
    // draws from - Escape is the one permanently protected key (also
    // already unreachable here in practice, since Update() intercepts and
    // consumes Escape, cancelling any in-progress capture, before this
    // ever runs - excluded explicitly anyway so the guarantee doesn't
    // rely solely on that ordering). Capturing (key, shiftHeld) directly,
    // rather than matching against a fixed list of pre-built options, is
    // what makes Shift+AnyKey work generically instead of just Shift+1-5.
    private void CaptureAbilityKey()
    {
        foreach (KeyCode key in AllKeyCodes)
        {
            if (key == KeyCode.None || key >= KeyCode.Mouse0 || key == KeyCode.Escape) continue;
            if (!Input.GetKeyDown(key)) continue;

            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            KeyBindingOption option = new KeyBindingOption(key, shiftHeld);

            for (int i = 0; i < PlayerProfile.AbilitySlots; i++)
            {
                KeyBindingOption? existing = Profile.GetSlotKey(i);
                if (existing.HasValue && existing.Value.Matches(option)) Profile.SetSlotKey(i, null);
            }
            if (!option.RequiresShift) UnbindMovementKey(option.Key);

            Profile.SetSlotKey(awaitingKeyForSlot, option);
            awaitingKeyForSlot = -1;
            return;
        }
    }

    private void CaptureMovementKey()
    {
        foreach (KeyCode key in AllKeyCodes)
        {
            // Mouse buttons and joystick codes sit at the end of the enum;
            // clicking the "Press a key..." button itself must not bind
            // Mouse0. Escape is the one permanently protected key - already
            // unreachable here in practice (see CaptureAbilityKey's comment
            // above), excluded explicitly anyway for the same reason.
            if (key == KeyCode.None || key >= KeyCode.Mouse0 || key == KeyCode.Escape) continue;
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
                // Rendered by MenuShellController (UI Toolkit), not IMGUI -
                // see RefreshPanelVisibility() for its Show()/Hide()/SetMode wiring.
                break;
            case Panel.Skills:
                // Rendered by SkillsPanelController (UI Toolkit), not IMGUI -
                // see RefreshPanelVisibility() for its Show()/Hide() wiring.
                break;
            case Panel.Gear:
                DrawGearPanel();
                break;
            case Panel.Appearance:
                DrawAppearancePanel();
                break;
            case Panel.Options:
                // Rendered by OptionsPanelController (UI Toolkit), not IMGUI -
                // see RefreshPanelVisibility() for its Show()/Hide() wiring.
                break;
            case Panel.Summon:
                // Rendered by SummonPanelController (UI Toolkit), not IMGUI -
                // see RefreshPanelVisibility() for its Show()/Hide() wiring.
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

    private void OpenPanel(Panel panel)
    {
        activePanel = panel;
        scrollPosition = Vector2.zero;
        appearanceTabIndex = 0;
        appearanceTabScrollPosition = Vector2.zero;
        if (panel == Panel.Skills) RebuildSkillsLists();
        RefreshPanelVisibility();
    }

    // Main/in-game menu shell, Summon, and Skills panels are rendered by
    // MenuShellController/SummonPanelController/SkillsPanelController (UI
    // Toolkit) - see RefreshPanelVisibility().

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
            string tooltip = equipped != null ? TooltipText.BuildItemTooltip(equipped) + "\n(click to unequip)" : $"{SlotShortNames[i]} (empty)";

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
            if (GUI.Button(rect, new GUIContent("X", TooltipText.BuildItemTooltip(item) + "\n(click to equip)"), icon))
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

}
