using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkBootstrap : MonoBehaviour
{
    private const string LocalAddress = "127.0.0.1";

    // Server-only: whether the lobby navigator has pressed Start. Read by
    // the connection-approval callback to decide whether a connecting client
    // gets a player object right away (after Start) or waits in the lobby
    // (before it). A plain static rather than LobbyState.Phase on purpose -
    // the host's own approval runs inside StartHost BEFORE any in-scene
    // NetworkObject (LobbyState included) is spawned, so reading LobbyState
    // there would null-ref. Set by LobbyState.StartGameRpc, cleared with the
    // rest of the server session state.
    public static bool GameStarted { get; private set; }

    public static void MarkGameStarted() => GameStarted = true;

    private UnityTransport transport;
    private string address;

    private void Start()
    {
        // Standalone builds otherwise remember whichever monitor the window
        // was last on/moved to (Unity caches this per-machine) - forcing it
        // onto the primary display every launch avoids that. Editor-only
        // (there's no "wrong monitor" for the Game view) and skipped in
        // batch mode (a dedicated server has no window at all).
        // Screen.MoveMainWindowTo takes a DisplayInfo (the current
        // multi-display API), not the older Display class - Display.displays
        // still exists but is no longer an accepted argument type here.
#if !UNITY_EDITOR
        if (!Application.isBatchMode)
        {
            List<DisplayInfo> displays = new List<DisplayInfo>();
            Screen.GetDisplayLayout(displays);
            if (displays.Count > 0) Screen.MoveMainWindowTo(displays[0], Vector2Int.zero);
        }
#endif

        transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        NetworkManager.Singleton.OnServerStopped += HandleServerStopped;
        NetworkManager.Singleton.OnClientStopped += HandleClientStopped;

        string saved = ProfileStore.Current.ServerAddress;
        address = string.IsNullOrWhiteSpace(saved) ? DefaultAddress() : saved;

#if UNITY_SERVER && !UNITY_EDITOR
        StartServer();
#endif
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnServerStopped -= HandleServerStopped;
        NetworkManager.Singleton.OnClientStopped -= HandleClientStopped;
    }

    private void HandleServerStopped(bool wasHost)
    {
        ResetServerSessionState();
        NetworkManager.Singleton.ConnectionApprovalCallback = null;
    }

    // The local client is gone (disconnected, or the host shut down):
    // without this the next connect in the same process would skip straight
    // past the lobby, since Entered is never otherwise cleared. MainMenu
    // notices LobbyState vanishing on its own and drops its lobby panels.
    private static void HandleClientStopped(bool wasHost)
    {
        TestingAreaGate.Entered = false;
    }

    // Every client is let in; whether it gets a player object depends only
    // on whether the lobby has started. Before Start nobody has one (host
    // included) - LobbyState.StartGameRpc spawns them all at once - and a
    // client joining afterwards is spawned straight in here. Clients never
    // invoke this; NGO also only invokes it at all when the scene's
    // NetworkManager has Connection Approval ticked (an Editor step).
    private static void HandleConnectionApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        response.Approved = true;
        response.CreatePlayerObject = GameStarted;
    }

    // Server-only bookkeeping kept in statics (one server process, not
    // per-object) has to start every session empty - statics otherwise
    // survive stopping and re-hosting within the same process (and, with
    // domain reload off, a whole Editor play session), leaving stale item/
    // spell claims that block players from their own picks, stale Arcane
    // Shield domes that block ranged attacks at empty ground, and a
    // "started" lobby that would spawn the next session's players on
    // connect instead of holding them in the lobby.
    //
    // Cleared when a session ENDS (and once at startup), deliberately not
    // on NetworkManager.OnServerStarted: StartHost runs the host's own
    // connection approval (which reads GameStarted) BEFORE that event
    // fires, and used to spawn the host's player there too.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetServerSessionState()
    {
        CharacterEquipment.ResetServerSessionState();
        PlayerAbilities.ResetServerSessionState();
        ArcaneShieldZones.Clear();
        GameStarted = false;
    }

    // The scene's transport is authored with the real server's address, which
    // is what a shipped build should dial. In the Editor - including
    // Multiplayer Play Mode virtual players - the sensible default is the
    // host running on this machine.
    private string DefaultAddress()
    {
        return Application.isEditor ? LocalAddress : transport.ConnectionData.Address;
    }

    // Shown from startup until a session exists - connecting comes first,
    // the lobby (see LobbyState/MainMenu) follows on the other side of it.
    private void OnGUI()
    {
        if (NetworkManager.Singleton == null) return;
        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer) return;

        DevGui.Begin();
        GUILayout.BeginArea(new Rect(10, 10, 260, 140));

        GUILayout.Label("Server address");
        GUILayout.BeginHorizontal();
        address = GUILayout.TextField(address);
        if (GUILayout.Button("Local", GUILayout.Width(50))) address = LocalAddress;
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Host")) StartHost();
        if (GUILayout.Button("Client")) StartClient();
        if (GUILayout.Button("Server")) StartServer();

        GUILayout.EndArea();
    }

    // The approval callback has to be in place before the server side comes
    // up: StartHost runs the host's own approval synchronously inside it.
    private static void StartHost()
    {
        NetworkManager.Singleton.ConnectionApprovalCallback = HandleConnectionApproval;
        NetworkManager.Singleton.StartHost();
    }

    private static void StartServer()
    {
        NetworkManager.Singleton.ConnectionApprovalCallback = HandleConnectionApproval;
        NetworkManager.Singleton.StartServer();
    }

    private void StartClient()
    {
        string target = address.Trim();
        if (target.Length == 0) target = DefaultAddress();

        transport.SetConnectionData(target, transport.ConnectionData.Port, transport.ConnectionData.ServerListenAddress);

        ProfileStore.Current.ServerAddress = target;
        ProfileStore.Save();

        NetworkManager.Singleton.StartClient();
    }
}
