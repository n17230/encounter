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

    private UIDocument document;
    private VisualElement root;
    private Button backButton;
    private VisualElement kitToolbar;
    private VisualElement kitSubRow;
    private Label kitSubRowLabel;
    private Button removeButton;
    private Button keyBindingButton;
    private VisualElement categoryTabs;
    private ScrollView availableScroll;

    private readonly List<VisualElement> kitSlotBoxes = new List<VisualElement>();
    private readonly List<Button> categoryTabButtons = new List<Button>();
    private IReadOnlyList<SkillSlotDisplay> lastSlots = Array.Empty<SkillSlotDisplay>();
    private IReadOnlyList<AbilityCategoryDisplay> lastCategories = Array.Empty<AbilityCategoryDisplay>();
    private int currentSelectedSlot = -1;
    // Which category tab is showing - purely a navigation/view concern with
    // no bearing on Profile/game data (unlike selectedSlot/awaitingKeyForSlot,
    // which MainMenu owns), so it's kept here rather than threaded through
    // MainMenu, the same way a ScrollView already manages its own scroll
    // offset without MainMenu's involvement.
    private int selectedCategoryIndex;

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
        categoryTabs = root.Q<VisualElement>("category-tabs");
        availableScroll = root.Q<ScrollView>("available-scroll");

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

    // Rebuilds both the kit toolbar and the available list from scratch -
    // called whenever the underlying data might have changed (panel opened,
    // an ability assigned/removed, or a key binding captured), not every
    // frame.
    public void RebuildLists(IReadOnlyList<AbilityCategoryDisplay> categories, IReadOnlyList<SkillSlotDisplay> slots)
    {
        EnsureInitialized();

        lastSlots = slots;
        lastCategories = categories;
        if (selectedCategoryIndex < 0 || selectedCategoryIndex >= categories.Count) selectedCategoryIndex = 0;

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
                Button clickable = new Button(() => SlotClicked?.Invoke(index));
                clickable.AddToClassList("kit-slot__button");

                Label name = new Label(slot.Ability.AbilityName);
                name.AddToClassList("kit-slot__name");
                Label key = new Label(slot.KeyLabel);
                key.AddToClassList("kit-slot__key");

                clickable.Add(name);
                clickable.Add(key);
                box.Add(clickable);
            }

            kitToolbar.Add(box);
            kitSlotBoxes.Add(box);
        }

        categoryTabs.Clear();
        categoryTabButtons.Clear();
        for (int i = 0; i < categories.Count; i++)
        {
            int index = i;
            Button tab = new Button(() => SelectCategoryTab(index)) { text = categories[i].CategoryName };
            tab.AddToClassList("button");
            tab.AddToClassList("category-tab");
            categoryTabs.Add(tab);
            categoryTabButtons.Add(tab);
        }

        RenderSelectedCategory();
    }

    private void SelectCategoryTab(int index)
    {
        selectedCategoryIndex = index;
        RenderSelectedCategory();
    }

    // Rebuilds only the card list for whichever tab is currently selected -
    // called on every RebuildLists() and whenever the tab selection changes.
    private void RenderSelectedCategory()
    {
        for (int i = 0; i < categoryTabButtons.Count; i++)
        {
            categoryTabButtons[i].EnableInClassList("category-tab--selected", i == selectedCategoryIndex);
        }

        availableScroll.Clear();
        if (selectedCategoryIndex < 0 || selectedCategoryIndex >= lastCategories.Count) return;

        foreach (AbilityData ability in lastCategories[selectedCategoryIndex].Abilities)
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
