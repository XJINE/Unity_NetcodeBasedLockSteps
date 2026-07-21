using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Netcode;

namespace NetcodeBasedLockSteps {
public class LockStepManagerDynamic : LockStepManagerBase<LockStepManagerDynamic.StepData>
{
    public struct StepData : INetworkSerializable, IStepData<StepData>, IEquatable<StepData>
    {
        public FixedList4096Bytes<byte> Bytes;

        public int StepCount { get; set; }
        public int BufferSize => 4096;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            var stepCount = StepCount;
            serializer.SerializeValue(ref stepCount);
            StepCount = stepCount;

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

        public StepData CreateStepData(int stepCount, FastBufferWriter writer)
        {
            var bytes = new FixedList4096Bytes<byte>();

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