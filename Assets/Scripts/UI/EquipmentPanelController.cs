using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Display data for one paper-doll equip slot - MainMenu computes this (it
// owns Profile reads), the controller only renders it.
public struct EquipmentSlotDisplay
{
    public EquipmentSlot Slot;
    public string Label;      // e.g. "Main" - matches the old IMGUI panel's SlotShortNames
    public ItemData Equipped; // null = empty
}

// One tab's worth of the inventory grid - one per equipment slot category
// (Head/Neck/Chest/.../Main/Off, Ring1+Ring2 collapsed into one "Rings" tab
// since a ring item's own Slot is always just the Ring1 category - see
// MainMenu.RebuildEquipmentLists). Always all of them, even if a category is
// currently empty - tabs don't disappear, same convention Skills uses.
public struct InventoryCategoryDisplay
{
    public string CategoryName;
    public List<ItemData> Items;
}

// UI Toolkit presentation for the Equipment panel. Purely a view, same
// discipline as every other migrated panel - MainMenu.cs owns all real
// state (Profile.EquipmentIds); this class only displays what it's told via
// Rebuild() and reports clicks back up via events. Clicking a paper-doll
// slot (filled or empty) jumps the inventory tab bar to that slot's
// category rather than unequipping it directly - MainMenu pins the exact
// physical slot clicked (see EquipSlotClicked) so Ring1 vs Ring2 stays
// disambiguated even though they share one "Rings" tab. Equipping/
// unequipping always happens from the inventory grid from there: a real
// item (InventoryItemClicked), or the always-present "None" pseudo-entry
// (UnequipClicked). Drag-and-drop equip is a deliberately separate future
// pass, not this one.
[RequireComponent(typeof(UIDocument))]
public class EquipmentPanelController : MonoBehaviour
{
    public event Action BackRequested;
    public event Action<EquipmentSlot> EquipSlotClicked;
    public event Action<ItemData> InventoryItemClicked;
    public event Action UnequipClicked;
    // Fired only when the user clicks a tab button directly - not when
    // SelectCategory() switches tabs programmatically (see its own doc
    // comment). MainMenu uses this to know when to drop its pinned slot.
    public event Action<int> CategoryTabClicked;

    private UIDocument document;
    private VisualElement root;
    private Button backButton;
    private VisualElement equipGrid;
    // Owns which inventory tab is showing - view state, see CategoryTabBar.
    private CategoryTabBar categoryTabs;
    private VisualElement inventoryGrid;
    private Label inventoryEmptyLabel;
    private HoverTooltip hoverTooltip;

    private readonly List<string> categoryNameScratch = new List<string>();
    private IReadOnlyList<InventoryCategoryDisplay> lastCategories = Array.Empty<InventoryCategoryDisplay>();

    private bool initialized;

    // Per-slot-category outline color, matching the old IMGUI panel's
    // GetSlotOutlineColor exactly - moved here unchanged since it's pure
    // presentation with no Profile access. Null = no outline.
    private static Color? GetSlotOutlineColor(EquipmentSlot slot)
    {
        if (slot.IsRing()) return Color.red;
        switch (slot)
        {
            case EquipmentSlot.Trinket: return Color.green;
            case EquipmentSlot.MainHand: return Color.cyan;
            case EquipmentSlot.OffHand: return new Color(0.6f, 0f, 1f);
            case EquipmentSlot.Necklace: return Color.yellow;
            case EquipmentSlot.Chest: return Color.blue;
            case EquipmentSlot.Boots: return Color.black;
            default: return null;
        }
    }

    private void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        document = GetComponent<UIDocument>();
        root = document.rootVisualElement;

        backButton = root.Q<Button>("back-button");
        equipGrid = root.Q<VisualElement>("equip-grid");
        categoryTabs = new CategoryTabBar(root.Q<VisualElement>("category-tabs"));
        categoryTabs.SelectionChanged += OnCategoryTabSelectionChanged;
        inventoryGrid = root.Q<VisualElement>("inventory-grid");
        inventoryEmptyLabel = root.Q<Label>("inventory-empty-label");

        // HoverTooltip's floating element is added directly to rootVisualElement,
        // which is NOT the named "theme-root" child the UXML/USS actually scope
        // --color-* custom properties under - without this, the tooltip renders
        // with unthemed defaults (black text, no background). See CLAUDE.md.
        root.AddToClassList("theme-root");
        hoverTooltip = new HoverTooltip(root);

        backButton.clicked += () => BackRequested?.Invoke();

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

    // Jumps to a category in response to a paper-doll click - silent
    // (does not raise CategoryTabClicked), so MainMenu's pinned-slot state
    // isn't cleared by its own programmatic switch.
    public void SelectCategory(int index)
    {
        EnsureInitialized();
        categoryTabs.SelectWithoutNotify(index);
        RenderSelectedCategory();
    }

    public int SelectedCategoryIndex => categoryTabs.SelectedIndex;

    private void OnCategoryTabSelectionChanged(int index)
    {
        RenderSelectedCategory();
        CategoryTabClicked?.Invoke(index);
    }

    // Rebuilds the equip grid and the inventory tab bar from scratch -
    // called whenever the underlying data might have changed (panel opened,
    // an equip/unequip happened), not every frame.
    public void Rebuild(IReadOnlyList<EquipmentSlotDisplay> slots, IReadOnlyList<InventoryCategoryDisplay> inventoryCategories)
    {
        EnsureInitialized();

        equipGrid.Clear();
        foreach (EquipmentSlotDisplay slot in slots)
        {
            EquipmentSlot capturedSlot = slot.Slot;
            string tooltip = slot.Equipped != null
                ? TooltipText.BuildItemTooltip(slot.Equipped) + "\n(click to change)"
                : $"{slot.Label} (empty)\n(click to equip)";
            equipGrid.Add(BuildSlotBox(slot.Label, slot.Equipped, GetSlotOutlineColor(slot.Slot), tooltip,
                () => EquipSlotClicked?.Invoke(capturedSlot)));
        }

        lastCategories = inventoryCategories;

        categoryNameScratch.Clear();
        foreach (InventoryCategoryDisplay category in inventoryCategories) categoryNameScratch.Add(category.CategoryName);
        categoryTabs.Rebuild(categoryNameScratch);

        RenderSelectedCategory();
    }

    // Rebuilds only the inventory grid for whichever tab is currently
    // selected - called on every Rebuild() and whenever the tab selection
    // changes.
    private void RenderSelectedCategory()
    {
        inventoryGrid.Clear();
        int selected = categoryTabs.SelectedIndex;
        List<ItemData> items = selected >= 0 && selected < lastCategories.Count
            ? lastCategories[selected].Items
            : null;

        inventoryGrid.Add(BuildNoneBox());

        if (items != null)
        {
            foreach (ItemData item in items)
            {
                ItemData capturedItem = item;
                string tooltip = TooltipText.BuildItemTooltip(item) + "\n(click to equip)";
                inventoryGrid.Add(BuildSlotBox(null, item, GetSlotOutlineColor(item.Slot), tooltip,
                    () => InventoryItemClicked?.Invoke(capturedItem)));
            }
        }
        inventoryEmptyLabel.style.display = items == null || items.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // The always-present pseudo-entry at the front of every category's
    // inventory grid - "unequip whatever's in the target slot". Plain text,
    // not a new icon asset.
    private VisualElement BuildNoneBox()
    {
        VisualElement box = new VisualElement();
        box.AddToClassList("equipment-slot");

        Button clickable = new Button(() => UnequipClicked?.Invoke());
        clickable.AddToClassList("equipment-slot__button");

        Label none = new Label("None");
        none.AddToClassList("equipment-slot__none-label");
        clickable.Add(none);

        box.Add(clickable);
        hoverTooltip.Attach(clickable, () => "(click to unequip)");
        return box;
    }

    // Shared box builder for both the paper-doll and the inventory grid - an
    // icon-filling square with an optional top-left slot-name tag and a
    // category outline color. label == null means "no slot-name tag" -
    // inventory items don't need one (the item's own name is in the
    // tooltip, and every physical equip slot already gets one).
    private VisualElement BuildSlotBox(string label, ItemData item, Color? outlineColor, string tooltip, Action onClick)
    {
        VisualElement box = new VisualElement();
        box.AddToClassList("equipment-slot");
        ApplyOutline(box, outlineColor);

        Button clickable = new Button(onClick);
        clickable.AddToClassList("equipment-slot__button");

        if (item != null)
        {
            Image icon = new Image { sprite = item.Icon, scaleMode = ScaleMode.ScaleToFit };
            icon.AddToClassList("equipment-slot__icon");
            clickable.Add(icon);
        }

        box.Add(clickable);

        if (!string.IsNullOrEmpty(label))
        {
            Label tag = new Label(label);
            tag.AddToClassList("equipment-slot__label");
            box.Add(tag);
        }

        hoverTooltip.Attach(clickable, () => tooltip);
        return box;
    }

    private static void ApplyOutline(VisualElement box, Color? color)
    {
        if (!color.HasValue) return;
        box.style.borderTopWidth = 2f;
        box.style.borderBottomWidth = 2f;
        box.style.borderLeftWidth = 2f;
        box.style.borderRightWidth = 2f;
        box.style.borderTopColor = color.Value;
        box.style.borderBottomColor = color.Value;
        box.style.borderLeftColor = color.Value;
        box.style.borderRightColor = color.Value;
    }
}
