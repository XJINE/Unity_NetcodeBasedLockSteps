using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Netcode;

namespace NetcodeBasedLockSteps {
public class LockStepManagerDynamic : LockStepManagerBase<LockStepManagerDynamic.StepData>
{
    public struct StepData : INetworkSerializable, IStepData<StepData>, IEquatable<StepData>
    {
        // NOTE:
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

        public StepData CreateStepData(int stepCount, FastBufferWriter writer)
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

        public unsafe byte* GetUnsafePtr()
        {
            return _bytes.Length == 0 ? null : (byte*)UnsafeUtility.AddressOf(ref _bytes.ElementAt(0));
        }

        public bool Equals(StepData other)
        {
            return _stepCount == other._stepCount && _bytes == other._bytes;
        }
    }
}}