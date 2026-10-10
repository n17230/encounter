using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Display data for one "Your Kit" toolbar slot - MainMenu computes all of
// this (it owns Profile reads), the controller only renders it.
public struct SkillSlotDisplay
{
    public AbilityData Ability; // null = empty slot
    public string KeyLabel;     // meaningful only when Ability != null - "Always On" / the bound key's display name / "Unbound"
    public bool CanSetKeyBinding;
}

// One tab's worth of the "Available Abilities" list (Auras/Heals/Melee/
// Damage Spells/Utility - see MainMenu.CategorizeAbility). Always all 5,
// even if a category is currently empty - tabs don't disappear, they just
// show an empty list.
public struct AbilityCategoryDisplay
{
    public string CategoryName;
    public List<AbilityData> Abilities;
}

// One player's row in the "Team Picks" column - MainMenu builds these
// (the local player's from the Profile, everyone else's from the lobby's
// synced entries); the controller only renders them.
public struct SkillsPicksRow
{
    public string Name;
    public bool IsLocal;
    public List<AbilityData> Abilities;
}

// UI Toolkit presentation for the Skills panel. Purely a view, same
// discipline as OptionsPanelController - MainMenu.cs owns all real state
// (Profile.SlotAbilityIds/SlotKeys, selectedSlot, awaitingKeyForSlot); this
// class only displays what it's told via RebuildLists()/RefreshSelection()
// and reports clicks back up via events. "Your Kit" renders as a horizontal
// toolbar mirroring the real in-game ability bar's shape, per the user's
// request to preview it the way it'll actually look during play.
[RequireComponent(typeof(UIDocument))]
public class SkillsPanelController : MonoBehaviour
{
    public event Action BackRequested;
    public event Action<AbilityData> AvailableAbilityClicked;
    public event Action<int> SlotClicked;
    public event Action<int> RemoveClicked;
    public event Action<int> SetKeyBindingClicked;
    public event Action<int, int> SlotReordered;

    private UIDocument document;
    private VisualElement root;
    private Button backButton;
    private VisualElement kitToolbar;
    private VisualElement kitSubRow;
    private Label kitSubRowLabel;
    private Button removeButton;
    private Button keyBindingButton;
    // Owns which category tab is showing - view state, see CategoryTabBar.
    private CategoryTabBar categoryTabs;
    private ScrollView availableScroll;
    private HoverTooltip hoverTooltip;
    private PicksSection picksSection;

    private readonly List<VisualElement> kitSlotBoxes = new List<VisualElement>();
    private readonly List<PicksRowDisplay> picksRowScratch = new List<PicksRowDisplay>();
    private readonly List<string> categoryNameScratch = new List<string>();
    private IReadOnlyList<SkillSlotDisplay> lastSlots = Array.Empty<SkillSlotDisplay>();
    private IReadOnlyList<AbilityCategoryDisplay> lastCategories = Array.Empty<AbilityCategoryDisplay>();
    private int currentSelectedSlot = -1;

    // Click-vs-drag state for the kit toolbar's reordering - a single drag
    // can only ever involve one slot at a time, so these live at the
    // controller level rather than per-element.
    private int dragSourceIndex = -1;
    private bool isDragging;
    private const float DragThreshold = 6f;

    private bool initialized;

    private void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        document = GetComponent<UIDocument>();
        root = document.rootVisualElement;

        backButton = root.Q<Button>("back-button");
        kitToolbar = root.Q<VisualElement>("kit-toolbar");
        kitSubRow = root.Q<VisualElement>("kit-subrow");
        kitSubRowLabel = root.Q<Label>("kit-subrow-label");
        removeButton = root.Q<Button>("remove-button");
        keyBindingButton = root.Q<Button>("key-binding-button");
        categoryTabs = new CategoryTabBar(root.Q<VisualElement>("category-tabs"));
        categoryTabs.SelectionChanged += _ => RenderSelectedCategory();
        availableScroll = root.Q<ScrollView>("available-scroll");

        // HoverTooltip's floating element is added directly to rootVisualElement,
        // which is NOT the named "theme-root" child the UXML/USS actually scope
        // --color-* custom properties under - without this, the tooltip renders
        // with unthemed defaults (black text, no background). See CLAUDE.md.
        root.AddToClassList("theme-root");
        hoverTooltip = new HoverTooltip(root);
        picksSection = new PicksSection(root.Q<ScrollView>("picks-scroll"), hoverTooltip, "picks-icon--ability");

        backButton.clicked += () => BackRequested?.Invoke();
        // Remove/Set Key Binding are single shared buttons (not one pair per
        // slot) - they act on whichever slot RefreshSelection last reported
        // as selected.
        removeButton.clicked += () => RemoveClicked?.Invoke(currentSelectedSlot);
        keyBindingButton.clicked += () => SetKeyBindingClicked?.Invoke(currentSelectedSlot);

        // Explicit, not just relying on the UXML/USS default - guarantees
        // the sub-row (and its currentSelectedSlot == -1 default) can never
        // be clickable before the first RefreshSelection() call lands.
        kitSubRow.style.display = DisplayStyle.None;

        root.style.display = DisplayStyle.None;
    }

    public void Show()
    {
        EnsureInitialized();
        root.style.scale = new StyleScale(new Scale(Vector3.one * UIScale.Value));
        root.style.display = DisplayStyle.Flex;
    }

    public void Hide()
    {
        EnsureInitialized();
        root.style.display = DisplayStyle.None;
    }

    // During the lobby only the navigator may leave this screen (their Back
    // takes everyone back to the Choice screen); everyone else's screen
    // follows the phase and has no Back at all. In-game it's always shown.
    public void SetBackVisible(bool visible)
    {
        EnsureInitialized();
        backButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // The picks column: every player's name with their picked abilities as
    // icons, each with the same tooltip as a kit slot. Rebuilt whenever the
    // synced picks change, not every frame.
    public void RebuildPicks(IReadOnlyList<SkillsPicksRow> rows)
    {
        EnsureInitialized();

        picksRowScratch.Clear();
        foreach (SkillsPicksRow row in rows)
        {
            List<PicksIcon> icons = new List<PicksIcon>();
            foreach (AbilityData ability in row.Abilities)
            {
                AbilityData captured = ability;
                icons.Add(new PicksIcon { Sprite = captured.Icon, Tooltip = () => BuildKitSlotTooltipText(captured) });
            }
            picksRowScratch.Add(new PicksRowDisplay { Name = row.Name, IsLocal = row.IsLocal, Icons = icons });
        }
        picksSection.Render(picksRowScratch);
    }

    // Rebuilds both the kit toolbar and the available list from scratch -
    // called whenever the underlying data might have changed (panel opened,
    // an ability assigned/removed, or a key binding captured), not every
    // frame.
    public void RebuildLists(IReadOnlyList<AbilityCategoryDisplay> categories, IReadOnlyList<SkillSlotDisplay> slots)
    {
        EnsureInitialized();

        lastSlots = slots;
        lastCategories = categories;

        kitToolbar.Clear();
        kitSlotBoxes.Clear();
        for (int i = 0; i < slots.Count; i++)
        {
            int index = i;
            SkillSlotDisplay slot = slots[i];

            VisualElement box = new VisualElement();
            box.AddToClassList("kit-slot");

            if (slot.Ability == null)
            {
                box.AddToClassList("kit-slot--empty");
                Label empty = new Label("Empty");
                empty.AddToClassList("kit-slot__empty-label");
                box.Add(empty);
            }
            else
            {
                // A plain VisualElement, not a Button - dragging needs to
                // suppress the "click" that a plain PointerDown+PointerUp
                // would otherwise fire, and Unity's built-in Button/Clickable
                // manipulator can't be reliably intercepted once a drag is
                // already under way. RegisterDragHandlers below drives both
                // the click (select/open the sub-row) and the drag itself.
                VisualElement clickable = new VisualElement();
                clickable.AddToClassList("kit-slot__button");

                Image icon = new Image { sprite = slot.Ability.Icon, scaleMode = ScaleMode.ScaleToFit };
                icon.AddToClassList("kit-slot__icon");
                Label key = new Label(slot.KeyLabel);
                key.AddToClassList("kit-slot__key");

                clickable.Add(icon);
                clickable.Add(key);
                box.Add(clickable);

                hoverTooltip.Attach(clickable, () => BuildKitSlotTooltipText(slot.Ability));
                RegisterDragHandlers(clickable, index);
            }

            kitToolbar.Add(box);
            kitSlotBoxes.Add(box);
        }

        categoryNameScratch.Clear();
        foreach (AbilityCategoryDisplay category in categories) categoryNameScratch.Add(category.CategoryName);
        categoryTabs.Rebuild(categoryNameScratch);

        RenderSelectedCategory();
    }

    // Click-to-select and drag-to-reorder on the same element: PointerDown
    // captures the pointer without deciding yet, PointerMove only commits to
    // "this is a drag" once the pointer has actually moved past a small
    // threshold (so a normal click still reaches SlotClicked), and PointerUp
    // fires whichever one actually happened.
    private void RegisterDragHandlers(VisualElement element, int index)
    {
        Vector2 dragStartPosition = default;

        element.RegisterCallback<PointerDownEvent>(evt =>
        {
            dragSourceIndex = index;
            dragStartPosition = evt.position;
            isDragging = false;
            element.CapturePointer(evt.pointerId);
        });

        element.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (dragSourceIndex != index || !element.HasPointerCapture(evt.pointerId)) return;

            if (!isDragging && Vector2.Distance(evt.position, dragStartPosition) > DragThreshold)
            {
                isDragging = true;
                kitSlotBoxes[index].AddToClassList("kit-slot--dragging");
            }

            if (isDragging) UpdateDropTargetHighlight(evt.position);
        });

        element.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (dragSourceIndex != index || !element.HasPointerCapture(evt.pointerId)) return;

            // Read everything needed BEFORE releasing the pointer -
            // ReleasePointer can synchronously fire PointerCaptureOutEvent,
            // whose handler (below) resets isDragging/dragSourceIndex, so
            // reading those fields after the release call isn't safe.
            bool wasDragging = isDragging;
            int dropIndex = wasDragging ? FindSlotIndexAt(evt.position) : -1;

            element.ReleasePointer(evt.pointerId);
            ClearDragVisuals();
            dragSourceIndex = -1;
            isDragging = false;

            if (wasDragging)
            {
                if (dropIndex >= 0 && dropIndex != index) SlotReordered?.Invoke(index, dropIndex);
            }
            else
            {
                SlotClicked?.Invoke(index);
            }
        });

        // Defensive reset for capture being lost some other way (e.g. the
        // panel closing mid-drag) - the normal PointerUp path above already
        // clears dragSourceIndex first, so this is a no-op on that path.
        element.RegisterCallback<PointerCaptureOutEvent>(_ =>
        {
            if (dragSourceIndex != index) return;
            ClearDragVisuals();
            dragSourceIndex = -1;
            isDragging = false;
        });
    }

    private int FindSlotIndexAt(Vector2 panelPosition)
    {
        for (int i = 0; i < kitSlotBoxes.Count; i++)
        {
            if (kitSlotBoxes[i].worldBound.Contains(panelPosition)) return i;
        }
        return -1;
    }

    private void UpdateDropTargetHighlight(Vector2 panelPosition)
    {
        int hoverIndex = FindSlotIndexAt(panelPosition);
        for (int i = 0; i < kitSlotBoxes.Count; i++)
        {
            kitSlotBoxes[i].EnableInClassList("kit-slot--drop-target", i == hoverIndex && i != dragSourceIndex);
        }
    }

    private void ClearDragVisuals()
    {
        for (int i = 0; i < kitSlotBoxes.Count; i++)
        {
            kitSlotBoxes[i].RemoveFromClassList("kit-slot--dragging");
            kitSlotBoxes[i].RemoveFromClassList("kit-slot--drop-target");
        }
    }

    // Rebuilds only the card list for whichever tab is currently selected -
    // called on every RebuildLists() and whenever the tab selection changes.
    private void RenderSelectedCategory()
    {
        availableScroll.Clear();
        int selected = categoryTabs.SelectedIndex;
        if (selected < 0 || selected >= lastCategories.Count) return;

        foreach (AbilityData ability in lastCategories[selected].Abilities)
        {
            Button card = new Button(() => AvailableAbilityClicked?.Invoke(ability));
            card.AddToClassList("ability-card");

            // Reserves the same slot whether or not this ability's Icon has
            // been wired yet (see IconWiringTool), so cards stay aligned
            // with each other either way instead of the text column
            // shifting left for not-yet-iconed abilities.
            Image icon = new Image { sprite = ability.Icon, scaleMode = ScaleMode.ScaleToFit };
            icon.AddToClassList("ability-card__icon");
            card.Add(icon);

            VisualElement textColumn = new VisualElement();
            textColumn.AddToClassList("ability-card__text-column");

            VisualElement nameRow = new VisualElement();
            nameRow.AddToClassList("ability-card__name-row");

            Label name = new Label(ability.AbilityName);
            name.AddToClassList("ability-card__name");
            nameRow.Add(name);

            // Aura spells have no cast/mana cost at all (see BuildAbilityBody) -
            // showing "Mana: 0" next to a passive would misleadingly imply it
            // can be cast for free rather than not cast at all. "(passive)"
            // in the same spot/style communicates that instead.
            Label meta = ability.IsAuraSpell
                ? new Label("(passive)")
                : new Label($"Mana: {ability.ManaCost}");
            meta.AddToClassList("ability-card__meta");
            nameRow.Add(meta);

            Label description = new Label(TooltipText.BuildAbilityBody(ability));
            description.AddToClassList("ability-card__description");

            textColumn.Add(nameRow);
            textColumn.Add(description);
            card.Add(textColumn);
            availableScroll.Add(card);
        }
    }

    // Same name/mana-or-passive/body content as an Available Abilities card,
    // just as one flat hover tooltip string instead of separate elements -
    // the kit slot itself only has room for an icon, not this much text.
    private static string BuildKitSlotTooltipText(AbilityData ability)
    {
        string meta = ability.IsAuraSpell ? "(passive)" : $"Mana: {ability.ManaCost}";
        return $"{ability.AbilityName} - {meta}\n\n{TooltipText.BuildAbilityBody(ability)}";
    }

    // Called once per frame while the panel is showing - only toggles the
    // already-built toolbar's selection highlight and the shared sub-row's
    // visibility/content, never rebuilds anything.
    public void RefreshSelection(int selectedSlot, int awaitingKeyForSlot)
    {
        EnsureInitialized();

        currentSelectedSlot = selectedSlot;

        for (int i = 0; i < kitSlotBoxes.Count; i++)
        {
            kitSlotBoxes[i].EnableInClassList("kit-slot--selected", i == selectedSlot);
        }

        bool validSelection = selectedSlot >= 0 && selectedSlot < lastSlots.Count && lastSlots[selectedSlot].Ability != null;
        kitSubRow.style.display = validSelection ? DisplayStyle.Flex : DisplayStyle.None;
        if (validSelection)
        {
            SkillSlotDisplay slot = lastSlots[selectedSlot];
            kitSubRowLabel.text = slot.Ability.AbilityName;
            keyBindingButton.style.display = slot.CanSetKeyBinding ? DisplayStyle.Flex : DisplayStyle.None;
            keyBindingButton.text = awaitingKeyForSlot == selectedSlot ? "Press a key..." : "Set Key Binding";
        }
    }
}
