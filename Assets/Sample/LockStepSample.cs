using Mirror;
using MirrorBasedLockSteps;
using UnityEngine;

public class LockStepSample : MonoBehaviour
{
    [SerializeField] private GameObject target;
    [SerializeField] private bool       move = true;

    private LockStepManager _lockStepManager;

    [System.Serializable]
    public struct StepDataSample: NetworkMessage
    {
        public Vector3 Position;
    }

    private void Start()
    {
        _lockStepManager             = FindAnyObjectByType<LockStepManager>();
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
            GUILayout.Label("Target : " + target.transform.position.ToString("F2"));
            GUILayout.Label("FPS : " + (1.0f / _lockStepManager.StepInterval).ToString("F2"));
        }
    }

    private byte[] GetData(int stepCount, NetworkWriter writer)
    {
        var stepData = new StepDataSample()
        {
            Position = move ? Random.insideUnitSphere : Vector3.zero
        };
        
        // Debug.Log($"1 : ({stepCount}) : {stepData.Position}");

        writer.Write(stepData);

        return writer.ToArray();
    }

    private void OnStep(int stepCount, NetworkReader reader)
    {
        var stepData = reader.Read<StepDataSample>();

        // Debug.Log($"2 : ({stepCount}) : {stepData.Position}");

        var nextPosition   = target.transform.position + stepData.Position;
            nextPosition.x = Mathf.Clamp(nextPosition.x, -3f, 3f);
            nextPosition.y = Mathf.Clamp(nextPosition.y, -3f, 3f);
            nextPosition.z = Mathf.Clamp(nextPosition.z, -3f, 3f);

        target.transform.position = nextPosition;
    }
}