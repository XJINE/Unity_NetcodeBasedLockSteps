using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Unity.Netcode;

namespace NetcodeBasedLockSteps {
public class LockStepManager : NetworkBehaviour
{
    public struct StepData : INetworkSerializable, IEquatable<StepData>
    {
        // CAUTION:
        // FixedList4096Bytes<byte>.Capacity is 4094, not 4096 (2 bytes are used for the length).

        private FixedList4096Bytes<byte> _bytes;
        private int                      _stepCount;

        public int StepCount  => _stepCount;
        public int BufferSize => _bytes.Capacity;
        public int Length     => _bytes.Length;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            var length = _bytes.Length;

            serializer.SerializeValue(ref _stepCount);
            serializer.SerializeValue(ref length);

            if (serializer.IsReader)
            {
                _bytes.Length = length;
            }

            if (length == 0)
            {
                return;
            }

            unsafe
            {
                if (serializer.IsWriter)
                {
                    serializer.GetFastBufferWriter().WriteBytesSafe(GetUnsafePtr(), length);
                }
                else
                {
                    serializer.GetFastBufferReader().ReadBytesSafe(GetUnsafePtr(), length);
                }
            }
        }

        public static StepData Create(int stepCount, FastBufferWriter writer)
        {
            var bytes = new FixedList4096Bytes<byte>();

            unsafe
            {
                bytes.AddRangeNoResize(writer.GetUnsafePtr(), writer.Length);
            }

            return new StepData
            {
                _bytes     = bytes,
                _stepCount = stepCount,
            };
        }

        public unsafe byte* GetUnsafePtr() // Points into this instance; keep it alive while reading.
        {
            return _bytes.Length == 0 ? null : (byte*)UnsafeUtility.AddressOf(ref _bytes.ElementAt(0));
        }

        public bool Equals(StepData other)
        {
            return _stepCount == other._stepCount && _bytes == other._bytes;
        }
    }

    public int   maxStepLogs        = 10000;
    public int   maxStepsPerFrame   = 3;
    public int   delaySteps         = 0;
    public float delayStepsInterval = 0.1f;

    public bool EnableSendStep { get; set; } = true; // For debugging.
    public bool EnableStep     { get; set; } = true; // For debugging.

    public int   StepCountInServer { get; private set; }
    public int   StepCountInClient { get; private set; }
    public float StepInterval      { get; private set; }

    public Func<FastBufferWriter, bool>  WriteStepDataFunc { get; set; }
    public Action<int, FastBufferReader> StepFunc          { get; set; }
    public Action                        RestartFunc       { get; set; }

    private FastBufferWriter _writer;
    private float            _lastProcessDelayStepTime;
    private float            _lastStepTime;
    private int              _restartCountInClient;

    private readonly NetworkList<StepData> _stepDataList = new();
    private readonly NetworkVariable<int>  _restartCount = new();

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
            _writer = new FastBufferWriter(default(StepData).BufferSize, Allocator.Persistent);
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

    public void Restart()
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

    private void Update()
    {
        if (!IsSpawned) // After Shutdown() the despawned NetworkList still holds data; avoid re-hitting the fatal path.
        {
            return;
        }

        if(NetworkManager.IsServer)
        {
            SendStep();
        }

        Step();
    }

    private void SendStep()
    {
        if (!EnableSendStep || WriteStepDataFunc is null)
        {
            return;
        }

        _writer.Seek(0);
        _writer.Truncate();

        if (!WriteStepDataFunc(_writer))
        {
            return;
        }

        if (default(StepData).BufferSize < _writer.Length)
        {
            Debug.LogError($"Step data exceeds buffer size: {_writer.Length} > {default(StepData).BufferSize}");
            return;
        }

        _stepDataList.Add(StepData.Create(StepCountInServer, _writer));

        while (maxStepLogs < _stepDataList.Count)
        {
            _stepDataList.RemoveAt(0);
        }

        StepCountInServer++;
    }

    private void Step()
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
                using var reader = new FastBufferReader(data.GetUnsafePtr(), Allocator.None, data.Length);
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