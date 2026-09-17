using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// UI Toolkit presentation for the Options panel - purely a view. All real
// state (the profile's movement keys, which row is currently awaiting a key
// press) stays owned by MainMenu.cs; this class only displays what it's told
// via Refresh() and reports clicks back up via events, so there's exactly
// one place ("MainMenu") that owns rebinding logic.
[RequireComponent(typeof(UIDocument))]
public class OptionsPanelController : MonoBehaviour
{
    public event Action BackRequested;
    public event Action ResetRequested;
    public event Action<int> KeyRowClicked;
    public event Action<float> UiScaleChanged;
    public event Action<float> LookSensitivityChanged;

    private UIDocument document;
    private VisualElement root;
    private Button backButton;
    private Label uiScaleLabel;
    private Slider uiScaleSlider;
    private Label lookSensitivityLabel;
    private Slider lookSensitivitySlider;
    private ScrollView keybindingsScroll;
    private Button resetButton;

    private readonly List<VisualElement> keyRows = new List<VisualElement>();
    private readonly List<Button> keyButtons = new List<Button>();

    private bool initialized;

    // Lazy rather than Awake/Start - UIDocument creates rootVisualElement in
    // its own OnEnable, and component init order across two scripts on the
    // same GameObject isn't guaranteed, so every public entry point resolves
    // on first use instead of assuming a specific Unity callback has already run.
    private void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        document = GetComponent<UIDocument>();
        root = document.rootVisualElement;

        backButton = root.Q<Button>("back-button");
        uiScaleLabel = root.Q<Label>("ui-scale-label");
        uiScaleSlider = root.Q<Slider>("ui-scale-slider");
        lookSensitivityLabel = root.Q<Label>("look-sensitivity-label");
        lookSensitivitySlider = root.Q<Slider>("look-sensitivity-slider");
        keybindingsScroll = root.Q<ScrollView>("keybindings-scroll");
        resetButton = root.Q<Button>("reset-button");

        backButton.clicked += () => BackRequested?.Invoke();
        resetButton.clicked += () => ResetRequested?.Invoke();
        uiScaleSlider.RegisterValueChangedCallback(evt => UiScaleChanged?.Invoke(evt.newValue));
        lookSensitivitySlider.RegisterValueChangedCallback(evt => LookSensitivityChanged?.Invoke(evt.newValue));

        root.style.display = DisplayStyle.None;
    }

    public void Show()
    {
        EnsureInitialized();
        root.style.display = DisplayStyle.Flex;
    }

    public void Hide()
    {
        EnsureInitialized();
        root.style.display = DisplayStyle.None;
    }

    // Builds the keybinding rows once - the action list/order never changes
    // at runtime (MovementAction is a fixed, append-only enum), so there's
    // nothing to rebuild later, only re-label via Refresh().
    public void BuildRows(string[] actionNames)
    {
        EnsureInitialized();

        keybindingsScroll.Clear();
        keyRows.Clear();
        keyButtons.Clear();

        for (int i = 0; i < actionNames.Length; i++)
        {
            int index = i;

            VisualElement row = new VisualElement();
            row.AddToClassList("keybind-row");
            if (i % 2 == 1) row.AddToClassList("keybind-row--alt");

            Label label = new Label(actionNames[i]);
            label.AddToClassList("keybind-row__label");

            Button keyButton = new Button(() => KeyRowClicked?.Invoke(index));
            keyButton.AddToClassList("button");
            keyButton.AddToClassList("keybind-row__key-button");

            row.Add(label);
            row.Add(keyButton);
            keybindingsScroll.Add(row);

            keyRows.Add(row);
            keyButtons.Add(keyButton);
        }
    }

    // Called once per frame while the panel is showing, with the current
    // display state MainMenu already tracks - this class never reads
    // ProfileStore/MovementInput itself.
    public void Refresh(KeyCode[] keys, int awaitingIndex, float uiScale, float lookSensitivity)
    {
        EnsureInitialized();

        root.style.scale = new StyleScale(new Scale(Vector3.one * uiScale));

        uiScaleLabel.text = $"UI Scale ({uiScale * 100f:0}%)";
        uiScaleSlider.SetValueWithoutNotify(uiScale);

        lookSensitivityLabel.text = $"Look Sensitivity ({lookSensitivity:0.00}x)";
        lookSensitivitySlider.SetValueWithoutNotify(lookSensitivity);

        for (int i = 0; i < keyButtons.Count && i < keys.Length; i++)
        {
            bool awaiting = awaitingIndex == i;
            keyRows[i].EnableInClassList("keybind-row--awaiting", awaiting);

            string label;
            if (awaiting) label = "Press a key...";
            else if (keys[i] == KeyCode.None) label = "Unbound";
            else label = keys[i].ToString();

            keyButtons[i].text = label;
        }
    }
}
