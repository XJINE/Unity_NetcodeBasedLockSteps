using System;
using Unity.Collections;
using UnityEngine;
using Unity.Netcode;

namespace NetcodeBasedLockSteps {

public abstract class LockStepManagerBase<TStepData> : NetworkBehaviour where TStepData : unmanaged, IEquatable<TStepData>
{
    public int   maxStepLogs        = 10000;
    public int   maxStepsPerFrame   = 3;
    public int   delaySteps         = 0;
    public float delayStepsInterval = 0.1f;

    private float _lastProcessDelayStepTime;
    private float _lastStepTime;

    private readonly NetworkList<TStepData> _stepDataList = new();

    public bool EnableSendStep { get; set; } = true; // For debugging.
    public bool EnableStep     { get; set; } = true; // For debugging.

    public int   StepCountInServer { get; private set; }
    public int   StepCountInClient { get; private set; }
    public float StepInterval      { get; private set; }

    public Func<INetworkSerializable>    GetDataFunc { get; set; }
    public Action<int, FastBufferReader> StepFunc    { get; set; }


    protected abstract int BufferSize { get; }
    protected abstract TStepData CreateStepData(int stepCount, FastBufferWriter writer);
    protected abstract int GetStepCount(in TStepData data);
    protected abstract NativeArray<byte> GetBytes(TStepData data, Allocator allocator);

    private void Start()
    {
        NetworkManager.OnClientConnectedCallback += OnClientConnected;
    }

    public override void OnDestroy()
    {
        if (NetworkManager is not null)
        {
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
        }

        base.OnDestroy();
    }

    private void Update()
    {
        if(NetworkManager.IsServer)
        {
            SendStep();
        }

        Step();
    }

    private void OnClientConnected(ulong clientId)
    {
        var missingFirstData = _stepDataList.Count != 0 && 0 < GetStepCount(_stepDataList[0]);

        if (!missingFirstData)
        {
            return;
        }

        NetworkManager.Shutdown();
    }

    private void SendStep()
    {
        if (!EnableSendStep || GetDataFunc is null)
        {
            return;
        }

        var data = GetDataFunc();

        if (data is null)
        {
            return;
        }

        using (var writer = new FastBufferWriter(BufferSize, Allocator.Temp))
        {
            writer.WriteNetworkSerializable(data);

            _stepDataList.Add(CreateStepData(StepCountInServer, writer));
        }

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

        if (GetStepCount(_stepDataList[index]) < StepCountInClient)
        {
            return;
        }

        var foundStep = false;

        for (; 0 <= index; index--)
        {
            if (GetStepCount(_stepDataList[index]) != StepCountInClient)
            {
                continue;
            }

            foundStep = true;

            break;
        }

        if (!foundStep)
        {
            Debug.Log("Required step is not in the data list.");
            return;
        }

        var limit = Mathf.Min(index + maxStepsPerFrame, _stepDataList.Count - delaySteps);

        if (limit <= index && delayStepsInterval < Time.time - _lastProcessDelayStepTime)
        {
            limit = Mathf.Min(index + 1, _stepDataList.Count);
            _lastProcessDelayStepTime = Time.time;
        }

        if (limit <= index)
        {
            return;
        }

        for (; index < limit; index++)
        {
            var data      = _stepDataList[index];
            var stepCount = GetStepCount(data);

            if (stepCount != StepCountInClient)
            {
                Debug.LogError($"Fatal Error in LockStep: Expected step {StepCountInClient}, but got {stepCount}");
                NetworkManager.Shutdown();
                return;
            }

            using (var reader = new FastBufferReader(GetBytes(data, Allocator.Temp), Allocator.None))
            {
                StepFunc(stepCount, reader);
            }

            StepCountInClient++;
        }

        StepInterval  = Time.timeSinceLevelLoad - _lastStepTime;
        _lastStepTime = Time.timeSinceLevelLoad;
    }
}}
