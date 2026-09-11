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
        transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

        string saved = ProfileStore.Current.ServerAddress;
        address = string.IsNullOrWhiteSpace(saved) ? DefaultAddress() : saved;

#if UNITY_SERVER && !UNITY_EDITOR
        NetworkManager.Singleton.StartServer();
#endif
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

        UIScale.Apply();
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
