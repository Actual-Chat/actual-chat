
namespace ActualChat.Serialization.Internal;

/// <summary>
/// Nerdbank.MessagePack converter for <see cref="Range{T}"/>. Array-encoded <c>[Start, End]</c>,
/// matching the hand-written <see cref="RangeMessagePackFormatter{T}"/>.
/// </summary>
public sealed class RangeNerdbankConverter<T> : MessagePackConverter<Range<T>>
    where T : notnull
{
    public override Range<T> Read(ref MessagePackReader reader, SerializationContext context)
    {
        var count = reader.ReadArrayHeader();
        if (count != 2)
            throw new MessagePackSerializationException($"Expected 2 items for Range<>, but got {count}.");

        var itemConverter = context.GetConverter<T>(context.TypeShapeProvider);
        var start = itemConverter.Read(ref reader, context)!;
        var end = itemConverter.Read(ref reader, context)!;
        return new Range<T>(start, end);
    }

    public override void Write(ref MessagePackWriter writer, in Range<T> value, SerializationContext context)
    {
        writer.WriteArrayHeader(2);
        var itemConverter = context.GetConverter<T>(context.TypeShapeProvider);
        itemConverter.Write(ref writer, value.Start, context);
        itemConverter.Write(ref writer, value.End, context);
    }
}
