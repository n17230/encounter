using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

// The row of category tabs shared by the Skills panel (ability categories)
// and the Equipment panel (inventory slot categories) - styled by Theme.uss's
// .category-tabs/.category-tab/.category-tab--selected. Plain C# helper
// owned by a panel controller, like HoverTooltip. Which tab is selected is
// pure view state (no bearing on Profile/game data), so it lives here
// rather than being threaded through MainMenu; it survives a Rebuild() as
// long as it's still a valid index.
public class CategoryTabBar
{
    private readonly VisualElement container;
    private readonly List<Button> buttons = new List<Button>();

    public int SelectedIndex { get; private set; }

    // Raised when the user picks a tab - not by Rebuild(), whose caller is
    // already about to re-render anyway.
    public event Action<int> SelectionChanged;

    public CategoryTabBar(VisualElement container)
    {
        this.container = container;
    }

    public void Rebuild(IReadOnlyList<string> categoryNames)
    {
        SelectedIndex = ClampSelection(SelectedIndex, categoryNames.Count);

        container.Clear();
        buttons.Clear();
        for (int i = 0; i < categoryNames.Count; i++)
        {
            int index = i;
            Button tab = new Button(() => Select(index)) { text = categoryNames[i] };
            tab.AddToClassList("button");
            tab.AddToClassList("category-tab");
            container.Add(tab);
            buttons.Add(tab);
        }

        ApplyHighlight();
    }

    // A selection that's no longer a valid index (the category list
    // shrank) falls back to the first tab. Pure + public for testing.
    public static int ClampSelection(int selectedIndex, int categoryCount)
    {
        return selectedIndex >= 0 && selectedIndex < categoryCount ? selectedIndex : 0;
    }

    // Jumps the selection programmatically without raising SelectionChanged -
    // lets a caller distinguish "the user clicked a tab" from a selection
    // change driven by something else (e.g. a paper-doll click switching
    // tabs on the user's behalf, which must not look like a genuine tab
    // click to whatever's listening for one).
    public void SelectWithoutNotify(int index)
    {
        SelectedIndex = ClampSelection(index, buttons.Count);
        ApplyHighlight();
    }

    private void Select(int index)
    {
        SelectedIndex = index;
        ApplyHighlight();
        SelectionChanged?.Invoke(index);
    }

    private void ApplyHighlight()
    {
        for (int i = 0; i < buttons.Count; i++)
        {
            buttons[i].EnableInClassList("category-tab--selected", i == SelectedIndex);
        }
    }
}
