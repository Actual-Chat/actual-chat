using System;
using System.Buffers;
using MessagePack;

namespace ActualChat.Streaming.Server
{
    public record RedisChannelOptions<T>
    {
        public string NewItemNotifyKeySuffix { get; init; } = "-new-item";
        public string StatusKey { get; init; } = "s";
        public string PartKey { get; init; } = "m";
        public string CompletedStatus { get; init; } = "completed";
        public TimeSpan WaitForNewMessageTimeout { get; init; } = TimeSpan.FromSeconds(0.25);

        public Action<T, IBufferWriter<byte>> Serializer { get; init; } =
            (item, bufferWriter) => MessagePackSerializer.Serialize(bufferWriter, item);
        public Func<ReadOnlyMemory<byte>, T> Deserializer { get; init; } =
            data => MessagePackSerializer.Deserialize<T>(data);
    }
}
