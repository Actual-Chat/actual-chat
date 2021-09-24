using System;
using System.Buffers;
using MessagePack;

namespace ActualChat.Serialization
{
    public class MessagePackByteSerializer<T> : ByteSerializer<T>
    {
        public new static MessagePackByteSerializer<T> Default { get; } = new();

        public override void Serialize(IBufferWriter<byte> bufferWriter, T value)
            => MessagePackSerializer.Serialize(bufferWriter, value);
        public override T Deserialize(ReadOnlyMemory<byte> data)
            => MessagePackSerializer.Deserialize<T>(data);
    }
}
