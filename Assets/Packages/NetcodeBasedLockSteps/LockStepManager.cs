using System;
using Unity.Collections;
using UnityEngine;
using Unity.Netcode;

namespace NetcodeBasedLockSteps {
public class LockStepManager : NetworkBehaviour
{
    private struct StepData : IEquatable<StepData>
    {
        public int                     StepCount;
        public FixedList512Bytes<byte> Bytes;

        public bool Equals(StepData other)
        {
            return StepCount == other.StepCount && Bytes == other.Bytes;
        }
    }

    public int   maxStepLogs        = 10000;
    public int   maxStepsPerFrame   = 3;
    public int   delaySteps         = 0;
    public float delayStepsInterval = 0.1f;

    private          float                 _lastProcessDelayStepTime;
    private readonly NetworkList<StepData> _stepDataList = new();

    public bool EnableSendStep    { get;         set; } = true; // For debugging.
    public bool EnableStep        { get;         set; } = true; // For debugging.
    public int  StepCountInServer { get; private set; }
    public int  StepCountInClient { get; private set; }

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
        if(NetworkManager.IsServer || NetworkManager.IsHost)
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

        using (var writer = new FastBufferWriter(512, Allocator.Temp))
        {
            unsafe
            {
                writer.WriteNetworkSerializable(data);

                var bytes = new FixedList512Bytes<byte>();
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
            NetworkManager.Shutdown();
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
                NetworkManager.Shutdown();
            }

            using (var reader = new FastBufferReader(data.Bytes.ToArray(), Allocator.Temp))
            {
                StepFunc(data.StepCount, reader);
            }

            StepCountInClient++;
        }
    }
}}