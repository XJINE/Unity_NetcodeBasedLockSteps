using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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

        // NOTE:
        // FixedList512Bytes<byte>.Capacity is 510, not 512 (2 bytes are used for the length).
        public int BufferSize => _bytes.Capacity;
        public int Length     => _bytes.Length;

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

        public unsafe byte* GetUnsafePtr()
        {
            return _bytes.Length == 0 ? null : (byte*)UnsafeUtility.AddressOf(ref _bytes.ElementAt(0));
        }

        public bool Equals(StepData other)
        {
            return StepCount == other.StepCount && _bytes == other._bytes;
        }
    }
}}