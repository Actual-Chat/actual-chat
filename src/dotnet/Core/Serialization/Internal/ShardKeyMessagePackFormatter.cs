using MessagePack.Formatters;

namespace ActualChat.Serialization.Internal;

// Core disables the MessagePack source generator (see Core.csproj), so this hand-written
// formatter is the only one ShardKey gets on non-dynamic (AOT) resolver chains.

/// <summary>
/// Writes <see cref="ShardKey"/> as a MessagePack [value, size] pair.
/// </summary>
public sealed class ShardKeyMessagePackFormatter : IMessagePackFormatter<ShardKey>
{
    public void Serialize(ref MessagePackWriter writer, ShardKey value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(2);
        writer.Write(value.Value);
        writer.Write(value.Size);
    }

    public ShardKey Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return default;

        var count = reader.ReadArrayHeader();
        if (count != 2)
            throw new MessagePackSerializationException($"Invalid {nameof(ShardKey)} array length: {count}.");

        var value = reader.ReadUInt32();
        var size = reader.ReadInt32();
        return new ShardKey(value, size);
    }
}
