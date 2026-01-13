using NetcodeBasedLockSteps;
using Unity.Netcode;
using UnityEngine;

public class LockStepSample : MonoBehaviour
{
    [SerializeField] private GameObject target;

    private NetworkManager  _networkManager;
    private LockStepManager _lockStepManager;
    private bool            _move = true;

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
            if (GUILayout.Button("Start as Host"))   { _networkManager.StartHost();   }
            if (GUILayout.Button("Start as Server")) { _networkManager.StartServer(); }
            if (GUILayout.Button("Start as Client")) { _networkManager.StartClient(); }
        }
        else
        {
            GUILayout.Label(isHost ? "Host" : isServer ? "Server" : "Client");
            GUILayout.Label("Step Count : " + (isServer ? _lockStepManager.StepCountInServer
                                                        : _lockStepManager.StepCountInClient));

            GUILayout.Label("Position : "      + target.transform.position             .ToString("F2"));
            GUILayout.Label("Step Interval : " + (_lockStepManager.StepInterval * 1000).ToString("F2"));
            GUILayout.Label("FPS : "           + ( 1 / _lockStepManager.StepInterval)  .ToString("F2"));

            if (isHost || isServer)
            {
                if (GUILayout.Button(_move ? "Stop" : "Move"))
                {
                    _move = !_move;
                }
            }
        }
    }

    private INetworkSerializable GetData()
    {
        return new StepData()
        {
            Position = _move ? Random.onUnitSphere : Vector3.zero
        };
    }

    private void OnStep(int stepCount, FastBufferReader reader)
    {
        reader.ReadValueSafe(out StepData stepData);

        var origin = target.transform.position += stepData.Position;

        if (origin.x < -3) { target.transform.position = new Vector3(-3, origin.y, origin.z); }
        if (3 < origin.x)  { target.transform.position = new Vector3( 3, origin.y, origin.z); }
        if (origin.y < -3) { target.transform.position = new Vector3(origin.x, -3, origin.z); }
        if (3 < origin.y)  { target.transform.position = new Vector3(origin.x,  3, origin.z); }
        if (origin.z < -3) { target.transform.position = new Vector3(origin.x, origin.y, -3); }
        if (3 < origin.z)  { target.transform.position = new Vector3(origin.x, origin.y,  3); }
    }
}