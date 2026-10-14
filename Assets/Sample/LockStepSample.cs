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
        public Vector3 RandomWalk;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref RandomWalk);
        }
    }

    private void Start()
    {
        _networkManager  = NetworkManager.Singleton;
        _lockStepManager = FindAnyObjectByType<LockStepManager>();

        _lockStepManager.WriteStepDataFunc = WriteStepData;
        _lockStepManager.StepFunc          = OnStep;
        _lockStepManager.ResetFunc         = OnReset;
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

            if (_lockStepManager.IsWaitingForReset)
            {
                GUILayout.Label("Waiting for reset...");
            }

            if (_lockStepManager.IsPaused)
            {
                GUILayout.Label("Paused");
            }

            GUILayout.Label("Step Count : " + (isServer ? _lockStepManager.StepCountInServer
                                                        : _lockStepManager.StepCountInClient));

            GUILayout.Label("Position : "      + target.transform.position             .ToString("F2"));
            GUILayout.Label("Step Interval : " + (_lockStepManager.StepInterval * 1000).ToString("F2"));
            GUILayout.Label("FPS : "           + ( 1 / _lockStepManager.StepInterval)  .ToString("F2"));

            if (isHost || isServer)
            {
                if (_lockStepManager.IsPaused)
                {
                    if (GUILayout.Button("Restart")) { _lockStepManager.Restart(); }
                }
                else
                {
                    if (GUILayout.Button("Pause"))   { _lockStepManager.Pause();   }
                }

                if (GUILayout.Button("Reset"))
                {
                    _lockStepManager.Reset();
                }
            }
        }
    }

    private bool WriteStepData(FastBufferWriter writer)
    {
        var data = new StepData
        {
            RandomWalk = Random.onUnitSphere
        };

        writer.WriteNetworkSerializable(data);

        return true;
    }

    private void OnStep(int stepCount, FastBufferReader reader)
    {
        reader.ReadValue(out StepData stepData);

        var nextPosition   = target.transform.position + stepData.RandomWalk;
            nextPosition.x = Mathf.Clamp(nextPosition.x, -3f, 3f);
            nextPosition.y = Mathf.Clamp(nextPosition.y, -3f, 3f);
            nextPosition.z = Mathf.Clamp(nextPosition.z, -3f, 3f);

        target.transform.position = nextPosition;
    }

    private void OnReset()
    {
        target.transform.position = Vector3.zero;
    }
}