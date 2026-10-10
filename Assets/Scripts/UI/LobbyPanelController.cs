using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// One row of the lobby's player list (Lobby and Choice panels both show
// it) - MainMenu builds these from LobbyState.Entries; the panels only
// render them.
public struct LobbyPlayerDisplay : IEquatable<LobbyPlayerDisplay>
{
    public ulong ClientId;
    public string Name;
    public bool Ready;
    public bool Admitted;
    public bool IsLocal;

    public bool Equals(LobbyPlayerDisplay other) =>
        ClientId == other.ClientId && Name == other.Name && Ready == other.Ready
        && Admitted == other.Admitted && IsLocal == other.IsLocal;
}

// Renders a player list into a container - shared by the Lobby and Choice
// panels so the two lists can't drift apart. Rebuilds only when the rows
// actually changed, since the panels refresh every frame they're open.
public class LobbyPlayerList
{
    private readonly VisualElement container;
    private readonly List<LobbyPlayerDisplay> lastRows = new List<LobbyPlayerDisplay>();
    private bool rendered;

    public LobbyPlayerList(VisualElement container)
    {
        this.container = container;
    }

    public void Refresh(IReadOnlyList<LobbyPlayerDisplay> rows)
    {
        if (rendered && SameAsLast(rows)) return;
        rendered = true;
        lastRows.Clear();
        lastRows.AddRange(rows);

        container.Clear();
        foreach (LobbyPlayerDisplay row in rows)
        {
            VisualElement rowElement = new VisualElement();
            rowElement.AddToClassList("player-row");
            if (row.IsLocal) rowElement.AddToClassList("player-row--local");

            Label name = new Label(row.Name);
            name.AddToClassList("player-row__name");
            rowElement.Add(name);

            // A waiter (joined mid-pick, not admitted) reads as Waiting
            // whatever their own Ready says - it isn't counted until the
            // team comes back to the main-menu screen.
            bool ready = row.Ready && row.Admitted;
            Label status = new Label(ready ? "Ready" : "Waiting");
            status.AddToClassList("player-row__status");
            if (ready) status.AddToClassList("player-row__status--ready");
            rowElement.Add(status);

            container.Add(rowElement);
        }
    }

    private bool SameAsLast(IReadOnlyList<LobbyPlayerDisplay> rows)
    {
        if (rows.Count != lastRows.Count) return false;
        for (int i = 0; i < rows.Count; i++)
        {
            if (!rows[i].Equals(lastRows[i])) return false;
        }
        return true;
    }
}

// Everything the Lobby panel shows besides what the player types - pushed
// in by MainMenu each frame the panel is open.
public struct LobbyPanelData
{
    public int PlayerCount;
    public bool IsReady;    // the local entry's synced Ready
    public bool IsAdmitted; // false = waiting for the team to come back to this screen
    public bool IsStarted;  // the game started without us (a waiter at Start)
    public IReadOnlyList<LobbyPlayerDisplay> Players;
}

// UI Toolkit presentation for the lobby's main-menu screen. Purely a view,
// same discipline as every other panel controller - MainMenu.cs owns the
// lobby state (via LobbyState) and the profile; this class only displays
// what it's told via Refresh()/SetName()/ShowNameError() and reports clicks
// via events. The name TextField is the one text field in any panel: it's
// only ever shown before a player exists, so IMGUI's Tab-focus gotcha (Tab
// is the tab-targeting key) can't bite, and it's blurred on Hide() anyway.
[RequireComponent(typeof(UIDocument))]
public class LobbyPanelController : MonoBehaviour
{
    public event Action DesignCharacterClicked;
    // The name as typed; MainMenu decides whether this is a Ready or an
    // Unready press from the synced entry.
    public event Action<string> ReadyClicked;

    private UIDocument document;
    private VisualElement root;
    private Label playersCountLabel;
    private TextField nameField;
    private Label nameErrorLabel;
    private Button designButton;
    private Button readyButton;
    private Label waitingLabel;
    private Label startedLabel;
    private LobbyPlayerList playerList;

    private bool initialized;

    private void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        document = GetComponent<UIDocument>();
        root = document.rootVisualElement;

        playersCountLabel = root.Q<Label>("players-count-label");
        nameField = root.Q<TextField>("name-field");
        nameErrorLabel = root.Q<Label>("name-error-label");
        designButton = root.Q<Button>("design-button");
        readyButton = root.Q<Button>("ready-button");
        waitingLabel = root.Q<Label>("waiting-label");
        startedLabel = root.Q<Label>("started-label");
        playerList = new LobbyPlayerList(root.Q<ScrollView>("player-list"));

        designButton.clicked += () => DesignCharacterClicked?.Invoke();
        readyButton.clicked += () => ReadyClicked?.Invoke(nameField.value);
        // Editing the name is the player's response to a rejection - the
        // old reason stops applying the moment the text changes.
        nameField.RegisterValueChangedCallback(_ => ShowNameError(null));

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
        // A hidden field must not keep keyboard focus - it would swallow
        // keys meant for whatever is showing instead.
        nameField.Blur();
        root.style.display = DisplayStyle.None;
    }

    // Pre-fills the field (the profile's last name) - called once when the
    // lobby appears, never per frame, so it can't overwrite typing.
    public void SetName(string name)
    {
        EnsureInitialized();
        nameField.SetValueWithoutNotify(name ?? "");
    }

    // The server's reason for refusing the name, under the field; null hides it.
    public void ShowNameError(string reason)
    {
        EnsureInitialized();
        nameErrorLabel.text = reason ?? "";
        nameErrorLabel.style.display = string.IsNullOrEmpty(reason) ? DisplayStyle.None : DisplayStyle.Flex;
    }

    // Called once per frame while the panel is showing.
    public void Refresh(LobbyPanelData data)
    {
        EnsureInitialized();

        playersCountLabel.text = $"Players: {data.PlayerCount}";

        // Ready is disabled until the name is well-formed (the server still
        // checks uniqueness); Unready must stay pressable whatever the field
        // currently holds. A waiter left behind at Start has nothing to
        // ready for any more.
        bool stuckOut = data.IsStarted && !data.IsAdmitted;
        readyButton.text = data.IsReady ? "Unready" : "Ready";
        readyButton.SetEnabled(!stuckOut && (data.IsReady || NameRules.IsWellFormed(nameField.value)));
        designButton.SetEnabled(!stuckOut);

        waitingLabel.style.display = !data.IsAdmitted && !data.IsStarted ? DisplayStyle.Flex : DisplayStyle.None;
        startedLabel.style.display = stuckOut ? DisplayStyle.Flex : DisplayStyle.None;

        playerList.Refresh(data.Players);
    }
}
