using Mirror;
using MirrorBasedLockSteps;
using UnityEngine;

public class LockStepSample : MonoBehaviour
{
    [SerializeField] private GameObject target;

    private LockStepManager _lockStepManager;
    private bool            _move = true;

    [System.Serializable]
    public struct StepDataSample: NetworkMessage
    {
        public Vector3 Position;
    }

    private void Start()
    {
        _lockStepManager = FindAnyObjectByType<LockStepManager>();
        _lockStepManager.GetDataFunc = GetData;
        _lockStepManager.StepFunc    = OnStep;
    }

    private void OnGUI()
    {
        if(NetworkClient.isConnected || NetworkServer.active)
        {
            GUILayout.Label("Time : "       + Time.timeSinceLevelLoad.ToString("F2"));
            GUILayout.Label("Step Count : " + (NetworkClient.active ? _lockStepManager.StepCountInClient
                                                                    : _lockStepManager.StepCountInServer));
        }
    }

    private byte[] GetData(NetworkWriter writer)
    {
        var stepData = new StepDataSample()
        {
            Position = _move ? Random.onUnitSphere : Vector3.zero
        };

        NetworkMessages.Pack(stepData, writer);
        return writer.ToArray();
    }

    private void OnStep(int stepCount, NetworkReader reader)
    {
        var stepData     = reader.Read<StepDataSample>();
        var nextPosition = target.transform.position + stepData.Position;
            nextPosition.x = Mathf.Clamp(nextPosition.x, -3f, 3f);
            nextPosition.y = Mathf.Clamp(nextPosition.y, -3f, 3f);
            nextPosition.z = Mathf.Clamp(nextPosition.z, -3f, 3f);

        target.transform.position = nextPosition;
    }
}