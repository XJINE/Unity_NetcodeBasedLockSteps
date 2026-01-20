using Mirror;
using UnityEngine;

public class NetworkManagerController : MonoBehaviour
{
    private void OnGUI()
    {
        var networkManager = NetworkManager.singleton;

        if(!NetworkClient.isConnected && !NetworkServer.active && !NetworkServer.activeHost)
        {
            if (GUILayout.Button("Start as Host"))   { networkManager.StartHost();   }
            if (GUILayout.Button("Start as Server")) { networkManager.StartServer(); }
            if (GUILayout.Button("Start as Client")) { networkManager.StartClient(); }
        }
    }
}