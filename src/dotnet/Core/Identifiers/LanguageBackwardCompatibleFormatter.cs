using ActualLab.Text.Internal;
using MemoryPack;
using MemoryPack.Internal;

namespace ActualChat;

public class LanguageBackwardCompatibleFormatter : MemoryPackFormatter<Language>
{
    public static readonly LanguageBackwardCompatibleFormatter Default = new ();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Serialize<TBufferWriter>(
        ref MemoryPackWriter<TBufferWriter> writer,
        scoped ref Language? value)
    {
        writer.WriteObjectHeader(1);
        var tempBuffer = ReusableLinkedArrayBufferWriterPool.Rent();
        try {
            var tempWriter = new MemoryPackWriter<ReusableLinkedArrayBufferWriter>(ref tempBuffer, writer.OptionalState);
            var sid = value?.Id.Value;
            tempWriter.WriteValueWithFormatter(StringAsSymbolMemoryPackFormatter.Default, sid);
            var offset = tempWriter.WrittenCount;

            tempWriter.Flush();
            tempWriter.WriteObjectHeader(1);
            writer.WriteVarInt(offset);
            tempBuffer.WriteToAndReset(ref writer);
        }
        finally {
            ReusableLinkedArrayBufferWriterPool.Return(tempBuffer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Deserialize(ref MemoryPackReader reader, scoped ref Language? value)
    {
        if (!reader.TryReadObjectHeader(out var count)) {
            value = null!;
            return;
        }

        Span<int> deltas = stackalloc int[count];
        for (int i = 0; i < count; i++)
            deltas[i] = reader.ReadVarIntInt32();

        if (count == 1) {
            if (deltas[0] != 0) {
                string? sid = null;
                StringAsSymbolMemoryPackFormatter.Default.Deserialize(ref reader, ref sid);
                if (!sid.IsNullOrEmpty())
                    value = Language.Parse(sid);
            }
            else
                value = null;
            return;
        }

        // Something is off, Language type should have just 1 delta
        value = null;
        for (int i = 1; i < count; i++)
            reader.Advance(deltas[i]);
    }
}

public class NullableLanguageBackwardCompatibleFormatter : MemoryPackFormatter<Language?>
{
    public static readonly NullableLanguageBackwardCompatibleFormatter Default = new ();

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
        LanguageBackwardCompatibleFormatter.Default.Serialize(ref writer, ref value);
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

        LanguageBackwardCompatibleFormatter.Default.Deserialize(ref reader, ref value);
    }
}
