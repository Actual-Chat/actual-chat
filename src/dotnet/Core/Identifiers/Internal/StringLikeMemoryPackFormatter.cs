namespace ActualChat.Internal;

public sealed class StringLikeMemoryPackFormatter<T> : MemoryPackFormatter<T>
    where T : IStringLike<T>
{
    public static readonly StringLikeMemoryPackFormatter<T> Default = new();

    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref T? value)
        => writer.WriteString(value?.Value);

    public override void Deserialize(ref MemoryPackReader reader, scoped ref T? value)
    {
        var s = reader.ReadString();
        value = s.IsNullOrEmpty() ? default : T.Parse(s);
    }
}
