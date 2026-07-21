using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Netcode;

namespace NetcodeBasedLockSteps {
public class LockStepManagerDynamic : LockStepManagerBase<LockStepManagerDynamic.StepData>
{
    public struct StepData : IEquatable<StepData>, INetworkSerializable
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

    protected override int BufferSize => 4096;

    protected override StepData CreateStepData(int stepCount, FastBufferWriter writer)
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

    protected override int GetStepCount(in StepData data)
    {
        return data.StepCount;
    }

    protected override NativeArray<byte> GetBytes(StepData data, Allocator allocator)
    {
        return data.Bytes.ToNativeArray(allocator);
    }
}}
