using Mirror;
using UnityEngine;

public class NetworkManagerController : MonoBehaviour
{
    private NetworkManager _networkManager;

    private void Start()
    {
        _networkManager  = NetworkManager.singleton;
    }

    private void OnGUI()
    {
        if(!NetworkClient.isConnected && !NetworkServer.active && !NetworkServer.activeHost)
        {
            if (GUILayout.Button("Start as Host"))   { _networkManager.StartHost();   }
            if (GUILayout.Button("Start as Server")) { _networkManager.StartServer(); }
            if (GUILayout.Button("Start as Client")) { _networkManager.StartClient(); }
        }
    }
}