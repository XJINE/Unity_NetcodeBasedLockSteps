using System;
using Mirror;
using UnityEngine;

namespace MirrorBasedLockSteps {
public class LockStepManager : NetworkBehaviour
{
    public struct StepData
    {
        public int    StepCount;
        public byte[] Bytes;
    }

    public int   maxStepLogs        = 10000;
    public int   maxStepsPerFrame   = 3;
    public int   delaySteps         = 0;
    public float delayStepsInterval = 0.1f;

    private float _lastProcessDelayStepTime;
    private float _lastStepTime;

    private readonly SyncList<StepData> _stepDataList = new();

    public bool EnableSendStep { get; set; } = true; // For debugging.
    public bool EnableStep     { get; set; } = true; // For debugging.

    public int   StepCountInServer { get; private set; }
    public int   StepCountInClient { get; private set; }
    public float StepInterval      { get; private set; }

    public Func  <int, NetworkWriter, byte[]> GetDataFunc { get; set; }
    public Action<int, NetworkReader>         StepFunc    { get; set; }

    private void Update()
    {
        if(NetworkServer.active)
        {
            SendStep();
        }

        Step();
    }

    // public override void OnServerConnect(NetworkConnectionToClient conn)
    // {
    //     base.OnServerConnect(conn);
    //
    //     var missingFirstData = _stepDataList.Count != 0 && 0 < _stepDataList[0].StepCount;
    //
    //     if (!missingFirstData)
    //     {
    //         return;
    //     }
    // }

    private void SendStep()
    {
        if (!EnableSendStep || GetDataFunc is null)
        {
            return;
        }

        using var writer = NetworkWriterPool.Get();
              var bytes  = GetDataFunc(StepCountInServer, writer);

        if (bytes == null || bytes.Length == 0)
        {
            return;
        }

        _stepDataList.Add(new StepData
        {
            StepCount = StepCountInServer,
            Bytes     = bytes
        });

        while (maxStepLogs < _stepDataList.Count)
        {
            _stepDataList.RemoveAt(0);
        }

        StepCountInServer++;
    }

    private void Step()
    {
        if (!EnableStep || _stepDataList.Count == 0 || StepFunc is null)
        {
            return;
        }

        var index = _stepDataList.Count - 1;

        if (_stepDataList[index].StepCount < StepCountInClient)
        {
            return;
        }

        var foundStep = false;

        for (; 0 <= index; index--)
        {
            if (_stepDataList[index].StepCount != StepCountInClient)
            {
                continue;
            }

            foundStep = true;

            break;
        }

        if (!foundStep)
        {
            Debug.Log("Required step is not in the data list.");
            // NetworkManager.Shutdown();
        }

        var limit = Mathf.Min(index + maxStepsPerFrame, _stepDataList.Count - delaySteps);

        if (limit <= index && delayStepsInterval < Time.time - _lastProcessDelayStepTime)
        {
            limit = Mathf.Min(index + 1, _stepDataList.Count);
            _lastProcessDelayStepTime = Time.time;
        }

        for (; index < limit; index++)
        {
            var data = _stepDataList[index];

            if (data.StepCount != StepCountInClient)
            {
                Debug.Log("Fatal Error in LockStep");
                // NetworkManager.Shutdown();
            }

            using (var reader = NetworkReaderPool.Get(data.Bytes))
            {
                StepFunc(data.StepCount, reader);
            }

            StepCountInClient++;
        }

        StepInterval  = Time.timeSinceLevelLoad - _lastStepTime;
        _lastStepTime = Time.timeSinceLevelLoad;
    }
}}