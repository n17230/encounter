using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkBootstrap : MonoBehaviour
{
    private const string LocalAddress = "127.0.0.1";

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

        string saved = ProfileStore.Current.ServerAddress;
        address = string.IsNullOrWhiteSpace(saved) ? DefaultAddress() : saved;

#if UNITY_SERVER && !UNITY_EDITOR
        NetworkManager.Singleton.StartServer();
#endif
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null) NetworkManager.Singleton.OnServerStopped -= HandleServerStopped;
    }

    private static void HandleServerStopped(bool wasHost) => ResetServerSessionState();

    // Server-only bookkeeping kept in statics (one server process, not
    // per-object) has to start every session empty - statics otherwise
    // survive stopping and re-hosting within the same process (and, with
    // domain reload off, a whole Editor play session), leaving stale item
    // claims that block players from their own equipment and stale Arcane
    // Shield domes that block ranged attacks at empty ground.
    //
    // Cleared when a session ENDS (and once at startup), deliberately not
    // on NetworkManager.OnServerStarted: StartHost spawns the host's own
    // player - which claims its equipment - BEFORE that event fires, so
    // clearing there would wipe the host's claims out from under it.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetServerSessionState()
    {
        CharacterEquipment.ResetServerSessionState();
        ArcaneShieldZones.Clear();
    }

    // The scene's transport is authored with the real server's address, which
    // is what a shipped build should dial. In the Editor - including
    // Multiplayer Play Mode virtual players - the sensible default is the
    // host running on this machine.
    private string DefaultAddress()
    {
        return Application.isEditor ? LocalAddress : transport.ConnectionData.Address;
    }

    private void OnGUI()
    {
        if (!TestingAreaGate.Entered) return;
        if (NetworkManager.Singleton == null) return;
        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer) return;

        DevGui.Begin();
        GUILayout.BeginArea(new Rect(10, 10, 260, 140));

        GUILayout.Label("Server address");
        GUILayout.BeginHorizontal();
        address = GUILayout.TextField(address);
        if (GUILayout.Button("Local", GUILayout.Width(50))) address = LocalAddress;
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Host")) NetworkManager.Singleton.StartHost();
        if (GUILayout.Button("Client")) StartClient();
        if (GUILayout.Button("Server")) NetworkManager.Singleton.StartServer();

        GUILayout.EndArea();
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
