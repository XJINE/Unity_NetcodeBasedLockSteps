using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Unity.Netcode;

namespace NetcodeBasedLockSteps {
public class LockStepManagerDynamic : NetworkBehaviour
{
    private struct StepData : IEquatable<StepData>, INetworkSerializable
    {
        public int                      StepCount;
        public FixedList4096Bytes<byte> Bytes;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref StepCount);

            if (serializer.IsWriter)
            {
                var array = Bytes.ToNativeArray(Allocator.Temp);
                serializer.SerializeValue(ref array, Allocator.Temp);
                array.Dispose();
            }
            else // IsReader
            {
                var array = default(NativeArray<byte>);

                serializer.SerializeValue(ref array, Allocator.Temp);

                Bytes = new FixedList4096Bytes<byte>();

                unsafe
                {
                    Bytes.AddRangeNoResize(array.GetUnsafeReadOnlyPtr(), array.Length);
                }

                array.Dispose();
            }
        }

        public bool Equals(StepData other)
        {
            return StepCount == other.StepCount && Bytes == other.Bytes;
        }
    }

    public int   maxStepLogs        = 10000;
    public int   maxStepsPerFrame   = 3;
    public int   delaySteps         = 0;
    public float delayStepsInterval = 0.1f;

    private float _lastProcessDelayStepTime;
    private float _lastStepTime;

    private readonly NetworkList<StepData> _stepDataList = new();

    public bool EnableSendStep { get; set; } = true; // For debugging.
    public bool EnableStep     { get; set; } = true; // For debugging.

    public int   StepCountInServer { get; private set; }
    public int   StepCountInClient { get; private set; }
    public float StepInterval      { get; private set; }

    public Func<INetworkSerializable>    GetDataFunc { get; set; }
    public Action<int, FastBufferReader> StepFunc    { get; set; }

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
        var missingFirstData = _stepDataList.Count != 0 && 0 < _stepDataList[0].StepCount;

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

        using (var writer = new FastBufferWriter(4096, Allocator.Temp))
        {
            unsafe
            {
                writer.WriteNetworkSerializable(data);

                var bytes = new FixedList4096Bytes<byte>();
                    bytes.AddRangeNoResize(writer.GetUnsafePtr(), writer.Length);

                _stepDataList.Add(new StepData
                {
                    StepCount = StepCountInServer,
                    Bytes     = bytes,
                });
            }
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
            var data = _stepDataList[index];

            if (data.StepCount != StepCountInClient)
            {
                Debug.LogError($"Fatal Error in LockStep: Expected step {StepCountInClient}, but got {data.StepCount}");
                NetworkManager.Shutdown();
                return;
            }

            using (var reader = new FastBufferReader(data.Bytes.ToNativeArray(Allocator.Temp), Allocator.None))
            {
                StepFunc(data.StepCount, reader);
            }

            StepCountInClient++;
        }

        StepInterval  = Time.timeSinceLevelLoad - _lastStepTime;
        _lastStepTime = Time.timeSinceLevelLoad;
    }
}}