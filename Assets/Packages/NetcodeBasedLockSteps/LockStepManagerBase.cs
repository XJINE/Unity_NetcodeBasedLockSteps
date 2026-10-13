using System;
using Unity.Collections;
using UnityEngine;
using Unity.Netcode;

namespace NetcodeBasedLockSteps {
public interface IStepData<out T> where T : unmanaged, IStepData<T>
{
    int StepCount  { get; }
    int BufferSize { get; }

    T CreateStepData(int stepCount, FastBufferWriter writer);
    NativeArray<byte> GetBytes(); // Do not define as a property since it requires alloc.
}

public abstract class LockStepManagerBase<TStepData> : NetworkBehaviour
    where TStepData : unmanaged, IEquatable<TStepData>, IStepData<TStepData>
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

    public Func<FastBufferWriter, bool>  WriteStepDataFunc { get; set; }
    public Action<int, FastBufferReader> StepFunc          { get; set; }

    private void Start()
    {
        NetworkManager.OnClientConnectedCallback += OnClientConnected;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        StepCountInServer         = 0;
        StepCountInClient         = 0;
        StepInterval              = 0f;
        _lastStepTime             = Time.time;
        _lastProcessDelayStepTime = Time.time;

        if (IsServer && _stepDataList.Count != 0)
        {
            _stepDataList.Clear();
        }
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
        // NOTE:
        // After Shutdown() the NetworkList still holds its data but the object is despawned.
        // Without this guard, Step() would hit the fatal path again every frame.
        if (!IsSpawned)
        {
            return;
        }

        if(NetworkManager.IsServer)
        {
            SendStep();
        }

        Step();
    }

    private void OnClientConnected(ulong clientId)
    {
        // NOTE:
        // This callback is invoked on the server for every client that connects,
        // and on a client only for its own connection.
        // The server alone decides whether a late joiner is accepted;
        // a rejected client is disconnected and receives the reason via NetworkManager.DisconnectReason.
        // The local client of a host is never disconnected (not allowed by NGO, and it already has every step).

        if (!NetworkManager.IsServer || clientId == NetworkManager.LocalClientId)
        {
            return;
        }

        var missingFirstData = _stepDataList.Count != 0 && 0 < _stepDataList[0].StepCount;

        if (!missingFirstData)
        {
            return;
        }

        Debug.LogWarning($"Client-{clientId} connected after step 0 was discarded. Late join is not supported, disconnecting it.");

        NetworkManager.DisconnectClient(clientId, "LockStep: late join is not supported.");
    }

    private void SendStep()
    {
        if (!EnableSendStep || WriteStepDataFunc is null)
        {
            return;
        }

        // NOTE:
        // The 'template' implementation isn't the cleanest,
        // but it's better to early-return to avoid breaking lockstep.

        var template = default(TStepData);

        using (var writer = new FastBufferWriter(template.BufferSize, Allocator.Temp))
        {
            if (!WriteStepDataFunc(writer))
            {
                return;
            }

            if (template.BufferSize < writer.Length)
            {
                Debug.LogError($"Step data exceeds buffer size: {writer.Length} > {template.BufferSize}");
                return;
            }

            _stepDataList.Add(template.CreateStepData(StepCountInServer, writer));
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
            Fatal($"Required step {StepCountInClient} is no longer in the data list (oldest: {_stepDataList[0].StepCount}).");
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
            var stepCount = data.StepCount;

            if (stepCount != StepCountInClient)
            {
                Fatal($"Expected step {StepCountInClient}, but got {stepCount}.");
                return;
            }

            using (var reader = new FastBufferReader(data.GetBytes(), Allocator.None))
            {
                StepFunc(stepCount, reader);
            }

            StepCountInClient++;
        }

        StepInterval  = Time.time - _lastStepTime;
        _lastStepTime = Time.time;
    }

    private void Fatal(string message)
    {
        Debug.LogError($"Fatal Error in LockStep: {message}");
        NetworkManager.Shutdown();
    }
}}