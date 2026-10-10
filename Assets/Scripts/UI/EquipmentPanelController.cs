using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// One equipped item in a player's "Team Picks" row - MainMenu computes
// these (it owns Profile reads and the lobby entries), the controller only
// renders them.
public struct EquipmentPickDisplay
{
    public EquipmentSlot Slot;
    public string SlotLabel; // e.g. "Ring 1" - matches the old IMGUI panel's SlotShortNames
    public ItemData Item;
}

public struct EquipmentPicksRow
{
    public string Name;
    public bool IsLocal;
    public List<EquipmentPickDisplay> Items;
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
// state (Profile.EquipmentIds, the lobby's synced picks); this class only
// displays what it's told via Rebuild() and reports clicks back up via
// events. Between the preview and the inventory sits the "Team Picks"
// section (PicksSection): every player's equipped items as icons. Clicking
// one of YOUR OWN icons jumps the inventory tab bar to that slot's category
// rather than unequipping it directly - MainMenu pins the exact physical
// slot clicked (see EquipSlotClicked) so Ring1 vs Ring2 stays disambiguated
// even though they share one "Rings" tab; other players' icons are tooltip
// only. Equipping/unequipping always happens from the inventory grid from
// there: a real item (InventoryItemClicked), or the always-present "None"
// pseudo-entry (UnequipClicked). An empty slot has no icon to click; the
// auto-resolve rule (first free ring, else the category's slot) covers it.
// Drag-and-drop equip is a deliberately separate future pass, not this one.
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
    // -1/0/1 = holding rotate-left/released/holding rotate-right - mirrors
    // the Appearance panel's GUI.RepeatButton hold-to-spin behavior, just
    // via pointer down/up/leave since UI Toolkit's Button has no repeat
    // button equivalent. MainMenu applies the actual rotation per-frame
    // (scaled by Time.deltaTime) since this controller has no reference to
    // CharacterPreview itself - same "raise, don't own state" rule every
    // other event here follows.
    public event Action<float> RotateDirectionChanged;

    private UIDocument document;
    private VisualElement root;
    private Button backButton;
    private PicksSection picksSection;
    // Owns which inventory tab is showing - view state, see CategoryTabBar.
    private CategoryTabBar categoryTabs;
    private VisualElement inventoryGrid;
    private Label inventoryEmptyLabel;
    private HoverTooltip hoverTooltip;

    // Same RenderTexture asset as MainMenu's own previewRenderTexture field -
    // CharacterPreview's camera is the single source of truth for both; a
    // second Editor-wiring since this is a different GameObject/component.
    // Optional like every other Editor-wired reference here: null just
    // leaves the box empty.
    [SerializeField] private RenderTexture previewRenderTexture;

    private readonly List<string> categoryNameScratch = new List<string>();
    private readonly List<PicksRowDisplay> picksRowScratch = new List<PicksRowDisplay>();
    private IReadOnlyList<InventoryCategoryDisplay> lastCategories = Array.Empty<InventoryCategoryDisplay>();

    private bool initialized;

    private void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        document = GetComponent<UIDocument>();
        root = document.rootVisualElement;

        backButton = root.Q<Button>("back-button");
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
        picksSection = new PicksSection(root.Q<ScrollView>("picks-scroll"), hoverTooltip, "picks-icon--item");

        VisualElement previewImage = root.Q<VisualElement>("preview-image");
        if (previewImage != null && previewRenderTexture != null)
        {
            previewImage.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(previewRenderTexture));
        }

        VisualElement rotateLeftButton = root.Q<VisualElement>("rotate-left-button");
        VisualElement rotateRightButton = root.Q<VisualElement>("rotate-right-button");
        AttachRotateHold(rotateLeftButton, -1f);
        AttachRotateHold(rotateRightButton, 1f);

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

    // Same rule as SkillsPanelController.SetBackVisible: navigator-only
    // during the lobby, always in-game.
    public void SetBackVisible(bool visible)
    {
        EnsureInitialized();
        backButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // Jumps to a category in response to a picks-icon click - silent
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

    // Rebuilds the picks section and the inventory tab bar from scratch -
    // called whenever the underlying data might have changed (panel opened,
    // an equip/unequip happened, another player's picks synced), not every
    // frame.
    public void Rebuild(IReadOnlyList<EquipmentPicksRow> picks, IReadOnlyList<InventoryCategoryDisplay> inventoryCategories)
    {
        EnsureInitialized();

        picksRowScratch.Clear();
        foreach (EquipmentPicksRow row in picks)
        {
            List<PicksIcon> icons = new List<PicksIcon>();
            foreach (EquipmentPickDisplay pick in row.Items)
            {
                EquipmentPickDisplay captured = pick;
                string tooltip = TooltipText.BuildItemTooltip(captured.Item);
                if (row.IsLocal) tooltip += "\n(click to change)";
                icons.Add(new PicksIcon
                {
                    Sprite = captured.Item.Icon,
                    Label = captured.SlotLabel,
                    Tooltip = () => tooltip,
                    OnClick = row.IsLocal ? () => EquipSlotClicked?.Invoke(captured.Slot) : (Action)null,
                });
            }
            picksRowScratch.Add(new PicksRowDisplay { Name = row.Name, IsLocal = row.IsLocal, Icons = icons });
        }
        picksSection.Render(picksRowScratch);

        lastCategories = inventoryCategories;

        categoryNameScratch.Clear();
        foreach (InventoryCategoryDisplay category in inventoryCategories) categoryNameScratch.Add(category.CategoryName);
        categoryTabs.Rebuild(categoryNameScratch);

        RenderSelectedCategory();
    }

    // A plain VisualElement, not a Button - same reasoning
    // SkillsPanelController.RegisterDragHandlers already documents: Unity's
    // Button carries its own built-in Clickable manipulator that competes
    // with custom pointer handling in ways that are hard to fully rely on.
    // Pointer down captures the pointer and starts rotating, up releases
    // and stops it (capture keeps this element receiving events even once
    // the cursor strays outside its bounds while held, so release is never
    // missed). PointerCaptureOutEvent is the defensive fallback for capture
    // being lost some other way (e.g. the panel closing mid-hold).
    private void AttachRotateHold(VisualElement element, float direction)
    {
        if (element == null) return;

        element.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0) return;
            element.CapturePointer(evt.pointerId);
            RotateDirectionChanged?.Invoke(direction);
        });

        element.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (!element.HasPointerCapture(evt.pointerId)) return;
            element.ReleasePointer(evt.pointerId);
            RotateDirectionChanged?.Invoke(0f);
        });

        element.RegisterCallback<PointerCaptureOutEvent>(_ => RotateDirectionChanged?.Invoke(0f));
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
                inventoryGrid.Add(BuildSlotBox(item, tooltip, () => InventoryItemClicked?.Invoke(capturedItem)));
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

    // One inventory-grid entry: an icon-filling square. The item's own name
    // is in the tooltip, so no slot-name tag is needed here.
    private VisualElement BuildSlotBox(ItemData item, string tooltip, Action onClick)
    {
        VisualElement box = new VisualElement();
        box.AddToClassList("equipment-slot");

        Button clickable = new Button(onClick);
        clickable.AddToClassList("equipment-slot__button");

        Image icon = new Image { sprite = item.Icon, scaleMode = ScaleMode.ScaleToFit };
        icon.AddToClassList("equipment-slot__icon");
        clickable.Add(icon);

        box.Add(clickable);

        hoverTooltip.Attach(clickable, () => tooltip);
        return box;
    }
}
