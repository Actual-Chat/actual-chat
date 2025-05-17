using MemoryPack;

namespace ActualChat.Serialization;

public class LegacyNullableLanguageFormatter : MemoryPackFormatter<Language?>
{
    public static readonly LegacyNullableLanguageFormatter Default = new ();

    public override void Serialize<TBufferWriter>(
        ref MemoryPackWriter<TBufferWriter> writer,
        scoped ref Language? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(1);
        LegacyLanguageFormatter.Default.Serialize(ref writer, ref value);
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref Language? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        if (count != 1)
            MemoryPackSerializationException.ThrowInvalidPropertyCount(1, count);

        LegacyLanguageFormatter.Default.Deserialize(ref reader, ref value);
    }
}
