using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Netcode;

namespace NetcodeBasedLockSteps {
public class LockStepManagerDynamic : LockStepManagerBase<LockStepManagerDynamic.StepData>
{
    public struct StepData : INetworkSerializable, IStepData<StepData>, IEquatable<StepData>
    {
        private FixedList4096Bytes<byte> _bytes;

        public int StepCount { get; private set; }
        public int BufferSize => 4096;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            var stepCount = StepCount;
            serializer.SerializeValue(ref stepCount);
            StepCount = stepCount;

            if (serializer.IsWriter)
            {
                var array = _bytes.ToNativeArray(Allocator.Temp);
                serializer.SerializeValue(ref array, Allocator.Temp);
                array.Dispose();
            }
            else // IsReader
            {
                var array = default(NativeArray<byte>);

                serializer.SerializeValue(ref array, Allocator.Temp);

                _bytes = new FixedList4096Bytes<byte>();

                unsafe
                {
                    _bytes.AddRangeNoResize(array.GetUnsafeReadOnlyPtr(), array.Length);
                }

                array.Dispose();
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