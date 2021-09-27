using System;
using System.Buffers;
using Microsoft.Toolkit.HighPerformance.Buffers;

namespace ActualChat.Serialization
{
    public abstract class ByteSerializer<T>
    {
        public static ByteSerializer<T> Default { get; } = MessagePackByteSerializer<T>.Default;

        public abstract void Serialize(IBufferWriter<byte> bufferWriter, T value);
        public abstract T Deserialize(ReadOnlyMemory<byte> data);

        public ArrayPoolBufferWriter<byte> Serialize(T value)
        {
            var bufferWriter = new ArrayPoolBufferWriter<byte>();
            Serialize(bufferWriter, value);
            return bufferWriter;
        }
    }
}
