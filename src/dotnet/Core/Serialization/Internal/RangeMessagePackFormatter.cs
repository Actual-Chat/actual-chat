using MessagePack.Formatters;

namespace ActualChat.Serialization.Internal;

public sealed class RangeMessagePackFormatter<T> : IMessagePackFormatter<Range<T>>
    where T : notnull
{
    public void Serialize(ref MessagePackWriter writer, Range<T> value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(2);
        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        formatter.Serialize(ref writer, value.Start, options);
        formatter.Serialize(ref writer, value.End, options);
    }

    public Range<T> Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 2)
            throw new MessagePackSerializationException($"Expected 2 items for Range<>, but got {count}.");

        var formatter = options.Resolver.GetFormatterWithVerify<T>();
        var start = formatter.Deserialize(ref reader, options);
        var end = formatter.Deserialize(ref reader, options);
        return new Range<T>(start, end);
    }
}
