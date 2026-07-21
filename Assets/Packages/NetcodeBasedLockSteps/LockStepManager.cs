using System;
using Unity.Collections;
using Unity.Netcode;

namespace NetcodeBasedLockSteps {
public class LockStepManager : LockStepManagerBase<LockStepManager.StepData>
{
    public struct StepData : IEquatable<StepData>, INetworkSerializeByMemcpy
    {
        // NOTE:
        // Ideally, the data size should be 1024 bytes due to UDP constraints,
        // but the next available size after 512 is 4096.

        public int                     StepCount;
        public FixedList512Bytes<byte> Bytes;

        public bool Equals(StepData other)
        {
            return StepCount == other.StepCount && Bytes == other.Bytes;
        }
    }

    protected override int BufferSize => 512;

    protected override StepData CreateStepData(int stepCount, FastBufferWriter writer)
    {
        var bytes = new FixedList512Bytes<byte>();

        unsafe
        {
            bytes.AddRangeNoResize(writer.GetUnsafePtr(), writer.Length);
        }

        return new StepData
        {
            StepCount = stepCount,
            Bytes     = bytes,
        };
    }

    protected override int GetStepCount(in StepData data)
    {
        return data.StepCount;
    }

    protected override NativeArray<byte> GetBytes(StepData data, Allocator allocator)
    {
        return data.Bytes.ToNativeArray(allocator);
    }
}}
