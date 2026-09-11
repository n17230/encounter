using Unity.Netcode;
using UnityEngine;

public class NetworkBootstrap : MonoBehaviour
{
    private void Start()
    {
#if UNITY_SERVER && !UNITY_EDITOR
        NetworkManager.Singleton.StartServer();
#endif
    }

    private void OnGUI()
    {
        if (!TestingAreaGate.Entered) return;
        if (NetworkManager.Singleton == null) return;
        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer) return;

        UIScale.Apply();
        GUILayout.BeginArea(new Rect(10, 10, 200, 100));
        if (GUILayout.Button("Host")) NetworkManager.Singleton.StartHost();
        if (GUILayout.Button("Client")) NetworkManager.Singleton.StartClient();
        if (GUILayout.Button("Server")) NetworkManager.Singleton.StartServer();
        GUILayout.EndArea();
    }
}
