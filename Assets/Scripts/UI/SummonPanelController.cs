using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// UI Toolkit presentation for the Summon Mobs panel. Purely a view, same
// discipline as OptionsPanelController - MainMenu.cs owns all real state
// (the selected mob index, the count, and PlayerSummon itself); this class
// only displays what it's told via SetMobList()/Refresh() and reports
// clicks back up via events.
[RequireComponent(typeof(UIDocument))]
public class SummonPanelController : MonoBehaviour
{
    public event Action BackRequested;
    public event Action<int> MobRowClicked;
    public event Action CountDecreaseRequested;
    public event Action CountIncreaseRequested;
    public event Action SummonRequested;
    public event Action SummonSkeletonsRequested;

    private UIDocument document;
    private VisualElement root;
    private Button backButton;
    private Label emptyLabel;
    private ScrollView mobScroll;
    private VisualElement countRow;
    private Button countMinusButton;
    private Label countLabel;
    private Button countPlusButton;
    private Button summonButton;
    private Button summonSkeletonsButton;

    private readonly List<Button> mobButtons = new List<Button>();

    private bool initialized;

    private void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        document = GetComponent<UIDocument>();
        root = document.rootVisualElement;

        backButton = root.Q<Button>("back-button");
        emptyLabel = root.Q<Label>("empty-label");
        mobScroll = root.Q<ScrollView>("mob-scroll");
        countRow = root.Q<VisualElement>("count-row");
        countMinusButton = root.Q<Button>("count-minus");
        countLabel = root.Q<Label>("count-label");
        countPlusButton = root.Q<Button>("count-plus");
        summonButton = root.Q<Button>("summon-button");
        summonSkeletonsButton = root.Q<Button>("summon-skeletons-button");

        backButton.clicked += () => BackRequested?.Invoke();
        countMinusButton.clicked += () => CountDecreaseRequested?.Invoke();
        countPlusButton.clicked += () => CountIncreaseRequested?.Invoke();
        summonButton.clicked += () => SummonRequested?.Invoke();
        summonSkeletonsButton.clicked += () => SummonSkeletonsRequested?.Invoke();

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

    // Rebuilt every time the panel opens rather than once at Start(), unlike
    // Options' fixed enum-driven row list - the local player (and therefore
    // PlayerSummon.SummonableMobs) doesn't exist yet at scene load, only
    // once the testing area's been entered, by which point this is safe.
    public void SetMobList(IReadOnlyList<string> mobLabels)
    {
        EnsureInitialized();

        mobScroll.Clear();
        mobButtons.Clear();

        for (int i = 0; i < mobLabels.Count; i++)
        {
            int index = i;
            Button button = new Button(() => MobRowClicked?.Invoke(index)) { text = mobLabels[i] };
            button.AddToClassList("mob-row");
            mobScroll.Add(button);
            mobButtons.Add(button);
        }
    }

    // Called once per frame while the panel is showing, with the current
    // display state MainMenu already tracks - this class never reads
    // PlayerSummon/ProfileStore itself.
    public void Refresh(bool hasPlayer, int selectedIndex, int count)
    {
        EnsureInitialized();

        DisplayStyle whenHasPlayer = hasPlayer ? DisplayStyle.Flex : DisplayStyle.None;
        emptyLabel.style.display = hasPlayer ? DisplayStyle.None : DisplayStyle.Flex;
        mobScroll.style.display = whenHasPlayer;
        countRow.style.display = whenHasPlayer;
        summonButton.style.display = whenHasPlayer;
        summonSkeletonsButton.style.display = whenHasPlayer;

        for (int i = 0; i < mobButtons.Count; i++)
        {
            mobButtons[i].EnableInClassList("mob-row--selected", i == selectedIndex);
        }

        countLabel.text = count.ToString();
    }
}
