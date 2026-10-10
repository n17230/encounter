using System;
using UnityEngine;
using UnityEngine.UIElements;

// UI Toolkit presentation for the in-game Escape menu's shell (replaces
// MainMenu's old DrawInGamePanel). Purely a view, same discipline as
// OptionsPanelController - MainMenu.cs owns activePanel/IsOpen and decides
// when to Show()/Hide() this; this class only reflects that and reports
// clicks via events. There is no pregame mode any more: before a session
// exists only NetworkBootstrap's connect panel shows, and the lobby
// (LobbyPanelController/ChoicePanelController) takes over from there.
[RequireComponent(typeof(UIDocument))]
public class MenuShellController : MonoBehaviour
{
    public event Action SkillsClicked;
    public event Action EquipmentClicked;
    public event Action AppearanceClicked;
    public event Action SummonClicked;
    public event Action OptionsClicked;
    public event Action RespawnClicked;
    public event Action ResumeClicked;
    public event Action ExitClicked;

    private UIDocument document;
    private VisualElement root;
    private Button skillsButton;
    private Button equipmentButton;
    private Button appearanceButton;
    private Button summonButton;
    private Button optionsButton;
    private Button respawnButton;
    private Button resumeButton;
    private Button exitButton;

    private bool initialized;

    // Lazy rather than Awake/Start - see OptionsPanelController for why
    // (UIDocument builds rootVisualElement in its own OnEnable, whose
    // ordering relative to another component's Awake isn't guaranteed).
    private void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        document = GetComponent<UIDocument>();
        root = document.rootVisualElement;

        skillsButton = root.Q<Button>("skills-button");
        equipmentButton = root.Q<Button>("equipment-button");
        appearanceButton = root.Q<Button>("appearance-button");
        summonButton = root.Q<Button>("summon-button");
        optionsButton = root.Q<Button>("options-button");
        respawnButton = root.Q<Button>("respawn-button");
        resumeButton = root.Q<Button>("resume-button");
        exitButton = root.Q<Button>("exit-button");

        skillsButton.clicked += () => SkillsClicked?.Invoke();
        equipmentButton.clicked += () => EquipmentClicked?.Invoke();
        appearanceButton.clicked += () => AppearanceClicked?.Invoke();
        summonButton.clicked += () => SummonClicked?.Invoke();
        optionsButton.clicked += () => OptionsClicked?.Invoke();
        respawnButton.clicked += () => RespawnClicked?.Invoke();
        resumeButton.clicked += () => ResumeClicked?.Invoke();
        exitButton.clicked += () => ExitClicked?.Invoke();

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
}
