using MemoryPack;

namespace ActualChat.Internal;

public sealed class StringIdentifierMemoryPackFormatter<TId> : MemoryPackFormatter<TId>
    where TId : StringIdentifier, IStringIdentifier<TId>
{
    public static readonly StringIdentifierMemoryPackFormatter<TId> Default = new();

    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref TId? value)
        => writer.WriteString(value?.Value);

    public override void Deserialize(ref MemoryPackReader reader, scoped ref TId? value)
    {
        var s = reader.ReadString();
        value = s.IsNullOrEmpty() ? null : TId.Parse(s);
    }
}
