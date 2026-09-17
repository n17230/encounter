using System;
using UnityEngine;
using UnityEngine.UIElements;

// UI Toolkit presentation for the top-level menu shell (replaces MainMenu's
// old DrawMainPanel/DrawInGamePanel). Purely a view, same discipline as
// OptionsPanelController - MainMenu.cs owns activePanel/IsOpen/
// TestingAreaGate.Entered and decides when to Show()/Hide() this and call
// SetMode(); this class only reflects that and reports clicks via events.
[RequireComponent(typeof(UIDocument))]
public class MenuShellController : MonoBehaviour
{
    public event Action SkillsClicked;
    public event Action GearClicked;
    public event Action AppearanceClicked;
    public event Action SummonClicked;
    public event Action OptionsClicked;
    public event Action RespawnClicked;
    public event Action EnterTestingAreaClicked;
    public event Action ResumeClicked;

    private UIDocument document;
    private VisualElement root;
    private Button skillsButton;
    private Button gearButton;
    private Button appearanceButton;
    private Button summonButton;
    private Button optionsButton;
    private Button respawnButton;
    private Button enterButton;
    private Button resumeButton;

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
        gearButton = root.Q<Button>("gear-button");
        appearanceButton = root.Q<Button>("appearance-button");
        summonButton = root.Q<Button>("summon-button");
        optionsButton = root.Q<Button>("options-button");
        respawnButton = root.Q<Button>("respawn-button");
        enterButton = root.Q<Button>("enter-button");
        resumeButton = root.Q<Button>("resume-button");

        skillsButton.clicked += () => SkillsClicked?.Invoke();
        gearButton.clicked += () => GearClicked?.Invoke();
        appearanceButton.clicked += () => AppearanceClicked?.Invoke();
        summonButton.clicked += () => SummonClicked?.Invoke();
        optionsButton.clicked += () => OptionsClicked?.Invoke();
        respawnButton.clicked += () => RespawnClicked?.Invoke();
        enterButton.clicked += () => EnterTestingAreaClicked?.Invoke();
        resumeButton.clicked += () => ResumeClicked?.Invoke();

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

    // Pregame vs the in-game Escape menu - only which extra buttons show
    // differs now (Summon Mobs/Respawn/Resume vs. Enter Testing Area);
    // Skills/Equipment/Character Creation/Options are the same either way.
    public void SetMode(bool inGame)
    {
        EnsureInitialized();

        DisplayStyle inGameOnly = inGame ? DisplayStyle.Flex : DisplayStyle.None;
        summonButton.style.display = inGameOnly;
        respawnButton.style.display = inGameOnly;
        resumeButton.style.display = inGameOnly;
        enterButton.style.display = inGame ? DisplayStyle.None : DisplayStyle.Flex;
    }
}
