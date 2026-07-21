using System;
using Unity.Collections;
using Unity.Netcode;

namespace NetcodeBasedLockSteps {
public class LockStepManager : LockStepManagerBase<LockStepManager.StepData>
{
    public struct StepData : INetworkSerializeByMemcpy, IStepData<StepData>, IEquatable<StepData>
    {
        // NOTE:
        // Ideally, the data size should be 1024 bytes due to UDP constraints,
        // but the next available size after 512 is 4096.
        public FixedList512Bytes<byte> Bytes;

        public int StepCount { get; set; }
        public int BufferSize => 512;

        public StepData CreateStepData(int stepCount, FastBufferWriter writer)
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

        public NativeArray<byte> GetBytes(Allocator allocator)
        {
            return Bytes.ToNativeArray(allocator);
        }

        public bool Equals(StepData other)
        {
            return StepCount == other.StepCount && Bytes == other.Bytes;
        }
    }
}}