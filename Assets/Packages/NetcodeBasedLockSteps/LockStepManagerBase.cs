using System;
using Unity.Collections;
using UnityEngine;
using Unity.Netcode;

namespace NetcodeBasedLockSteps {
public interface IStepData<out T> where T : unmanaged, IStepData<T>
{
    int StepCount  { get; }
    int BufferSize { get; }
    int Length     { get; }

    T CreateStepData(int stepCount, FastBufferWriter writer);

    unsafe byte* GetUnsafePtr(); // Points into this instance; keep it alive while reading.
}

public abstract class LockStepManagerBase : NetworkBehaviour
{
    public int   maxStepLogs        = 10000;
    public int   maxStepsPerFrame   = 3;
    public int   delaySteps         = 0;
    public float delayStepsInterval = 0.1f;

    public bool EnableSendStep { get; set; } = true; // For debugging.
    public bool EnableStep     { get; set; } = true; // For debugging.

    public int   StepCountInServer { get; protected set; }
    public int   StepCountInClient { get; protected set; }
    public float StepInterval      { get; protected set; }

    public Func<FastBufferWriter, bool>  WriteStepDataFunc { get; set; }
    public Action<int, FastBufferReader> StepFunc          { get; set; }
    public Action                        RestartFunc       { get; set; }

    public abstract void Restart();

    protected abstract void SendStep();
    protected abstract void Step();

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

    protected void Fatal(string message)
    {
        Debug.LogError($"Fatal Error in LockStep: {message}");
        NetworkManager.Shutdown();
    }
}

public abstract class LockStepManagerBase<TStepData> : LockStepManagerBase
    where TStepData : unmanaged, IEquatable<TStepData>, IStepData<TStepData>
{
    private float _lastProcessDelayStepTime;
    private float _lastStepTime;

    private readonly NetworkList<TStepData> _stepDataList = new();
    private readonly NetworkVariable<int>   _restartCount = new();

    private int _restartCountInClient;

    private FastBufferWriter _writer;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        StepCountInServer     = 0;
        _restartCountInClient = _restartCount.Value;

        ResetStepCountInClient();

        if (!IsServer)
        {
            return;
        }

        if (_stepDataList.Count != 0)
        {
            _stepDataList.Clear();
        }

        if (!_writer.IsInitialized)
        {
            _writer = new FastBufferWriter(default(TStepData).BufferSize, Allocator.Persistent);
        }

        NetworkManager.OnClientConnectedCallback += OnClientConnected;
    }

    public override void OnNetworkDespawn()
    {
        NetworkManager.OnClientConnectedCallback -= OnClientConnected;

        if (_writer.IsInitialized)
        {
            _writer.Dispose();
        }

        base.OnNetworkDespawn();
    }

    private void OnClientConnected(ulong clientId)
    {
        if (clientId == NetworkManager.LocalClientId)
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

    public override void Restart()
    {
        if (!IsServer)
        {
            Debug.LogWarning("Restart() is only available on the server.");
            return;
        }

        _stepDataList.Clear();
        StepCountInServer = 0;
        _restartCount.Value++;
    }

    private void ResetStepCountInClient()
    {
        StepCountInClient         = 0;
        StepInterval              = 0f;
        _lastStepTime             = Time.time;
        _lastProcessDelayStepTime = Time.time;
    }

    protected override void SendStep()
    {
        if (!EnableSendStep || WriteStepDataFunc is null)
        {
            return;
        }

        // NOTE:
        // The 'template' implementation isn't the cleanest,
        // but it's better to early-return to avoid breaking lockstep.

        var template = default(TStepData);

        _writer.Seek(0);
        _writer.Truncate();

        if (!WriteStepDataFunc(_writer))
        {
            return;
        }

        if (template.BufferSize < _writer.Length)
        {
            Debug.LogError($"Step data exceeds buffer size: {_writer.Length} > {template.BufferSize}");
            return;
        }

        _stepDataList.Add(template.CreateStepData(StepCountInServer, _writer));

        while (maxStepLogs < _stepDataList.Count)
        {
            _stepDataList.RemoveAt(0);
        }

        StepCountInServer++;
    }

    protected override void Step()
    {
        if (_restartCountInClient != _restartCount.Value)
        {
            _restartCountInClient = _restartCount.Value;
            ResetStepCountInClient();
            RestartFunc?.Invoke();
        }

        if (!EnableStep || _stepDataList.Count == 0 || StepFunc is null)
        {
            return;
        }

        var oldestStepCount = _stepDataList[0].StepCount;
        var index           = StepCountInClient - oldestStepCount;

        if (index < 0)
        {
            Fatal($"Required step {StepCountInClient} is no longer in the data list (oldest: {oldestStepCount}).");
            return;
        }

        while (index < _stepDataList.Count && _stepDataList[index].StepCount < StepCountInClient)
        {
            index++;
        }

        if (_stepDataList.Count <= index)
        {
            return; // Not arrived yet.
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

        for (; index < limit && index < _stepDataList.Count; index++) // Restart() may be called inside StepFunc.
        {
            var data      = _stepDataList[index];
            var stepCount = data.StepCount;

            if (stepCount < StepCountInClient)
            {
                continue;
            }

            if (stepCount != StepCountInClient)
            {
                Fatal($"Expected step {StepCountInClient}, but got {stepCount}.");
                return;
            }

            unsafe
            {
                using (var reader = new FastBufferReader(data.GetUnsafePtr(), Allocator.None, data.Length))
                {
                    StepFunc(stepCount, reader);
                }
            }

            StepCountInClient++;
        }

        StepInterval  = Time.time - _lastStepTime;
        _lastStepTime = Time.time;
    }
}}