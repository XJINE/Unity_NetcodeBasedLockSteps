using NetcodeBasedLockSteps;
using Unity.Netcode;
using UnityEngine;

public class LockStepSample : MonoBehaviour
{
    [SerializeField] private GameObject target;

    private NetworkManager  _networkManager;
    private LockStepManager _lockStepManager;

    private struct StepData : INetworkSerializable
    {
        public Vector3 Position;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
        }
    }

    private void Start()
    {
        _networkManager  = NetworkManager.Singleton;
        _lockStepManager = FindAnyObjectByType<LockStepManager>();

        _lockStepManager.GetDataFunc = GetData;
        _lockStepManager.StepFunc    = OnStep;
    }

    private void OnGUI()
    {
        var isHost   = _networkManager.IsHost;
        var isServer = _networkManager.IsServer;

        if(!_networkManager.IsConnectedClient && !isServer)
        {
            // GUILayout.Label("Waiting");
            if (GUILayout.Button("Start as Host"))   { _networkManager.StartHost();   }
            if (GUILayout.Button("Start as Server")) { _networkManager.StartServer(); }
            if (GUILayout.Button("Start as Client")) { _networkManager.StartClient(); }
        }
        else
        {
            GUILayout.Label(isHost ? "Host" : isServer ? "Server" : "Client");
            GUILayout.Label("Step Count : " + (isServer ? _lockStepManager.StepCountInServer
                                                        : _lockStepManager.StepCountInClient));
        }
    }

    private INetworkSerializable GetData()
    {
        return new StepData()
        {
            Position = Random.onUnitSphere
        };
    }

    private void OnStep(int stepCount, FastBufferReader reader)
    {
        reader.ReadValueSafe(out StepData stepData);
        target.transform.position += stepData.Position;
    }
}