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
        private FixedList512Bytes<byte> _bytes;

        public int StepCount { get; private set; }
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
                _bytes    = bytes,
                StepCount = stepCount,
            };
        }

        public NativeArray<byte> GetBytes()
        {
            return _bytes.ToNativeArray(Allocator.Temp);
        }

        public bool Equals(StepData other)
        {
            return StepCount == other.StepCount && _bytes == other._bytes;
        }
    }
}}