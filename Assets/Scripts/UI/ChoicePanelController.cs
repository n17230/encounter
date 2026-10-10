using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// UI Toolkit presentation for the lobby's Choice screen (Spells / Equipment
// / Start / Back to main menu, plus the player list). Purely a view, same
// discipline as every other panel controller - MainMenu.cs decides who the
// navigator is from LobbyState and what each click sends; this class only
// shows the buttons enabled for the navigator and disabled (with "<name> is
// choosing") for everyone else, and reports clicks via events. The server
// rejects any non-navigator call regardless, so the disabling is only
// feedback, not the enforcement.
[RequireComponent(typeof(UIDocument))]
public class ChoicePanelController : MonoBehaviour
{
    public event Action SpellsClicked;
    public event Action EquipmentClicked;
    public event Action StartClicked;
    public event Action BackToMainMenuClicked;

    private UIDocument document;
    private VisualElement root;
    private Label navigatorLabel;
    private Button spellsButton;
    private Button equipmentButton;
    private Button startButton;
    private Button backButton;
    private LobbyPlayerList playerList;

    private bool initialized;

    private void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        document = GetComponent<UIDocument>();
        root = document.rootVisualElement;

        navigatorLabel = root.Q<Label>("navigator-label");
        spellsButton = root.Q<Button>("spells-button");
        equipmentButton = root.Q<Button>("equipment-button");
        startButton = root.Q<Button>("start-button");
        backButton = root.Q<Button>("back-button");
        playerList = new LobbyPlayerList(root.Q<ScrollView>("player-list"));

        spellsButton.clicked += () => SpellsClicked?.Invoke();
        equipmentButton.clicked += () => EquipmentClicked?.Invoke();
        startButton.clicked += () => StartClicked?.Invoke();
        backButton.clicked += () => BackToMainMenuClicked?.Invoke();

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

    // Called once per frame while the panel is showing. Start is the one
    // button only the navigator sees at all; the others stay visible but
    // disabled for everyone else, so they can see what's being chosen from.
    public void Refresh(bool isNavigator, string navigatorName, IReadOnlyList<LobbyPlayerDisplay> players)
    {
        EnsureInitialized();

        spellsButton.SetEnabled(isNavigator);
        equipmentButton.SetEnabled(isNavigator);
        backButton.SetEnabled(isNavigator);
        startButton.style.display = isNavigator ? DisplayStyle.Flex : DisplayStyle.None;

        navigatorLabel.text = $"{navigatorName} is choosing";
        navigatorLabel.style.display = isNavigator ? DisplayStyle.None : DisplayStyle.Flex;

        playerList.Refresh(players);
    }
}
