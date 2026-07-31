using MessagePack.Formatters;

namespace ActualChat.Serialization.Internal;

// Core disables the MessagePack source generator (see Core.csproj), so this hand-written
// formatter is the only one ShardKey gets on non-dynamic (AOT) resolver chains.
// It deliberately drops the [Key(0)] array-of-1 layout DynamicObjectResolver used to emit
// (0x91 <int>) in favor of a bare integer, which makes ShardKey wire-compatible with int.

/// <summary>
/// Writes <see cref="ShardKey"/> as a bare MessagePack integer.
/// </summary>
public sealed class ShardKeyMessagePackFormatter : IMessagePackFormatter<ShardKey>
{
    public void Serialize(ref MessagePackWriter writer, ShardKey value, MessagePackSerializerOptions options)
        => writer.Write(value.Value);

    public ShardKey Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        => reader.TryReadNil() ? default : new ShardKey(reader.ReadInt32());
}
