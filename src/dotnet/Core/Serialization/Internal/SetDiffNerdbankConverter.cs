
namespace ActualChat.Serialization.Internal;

/// <summary>
/// Nerdbank.MessagePack converter for <see cref="SetDiff{TItem}"/>: <c>[AddedItems[], RemovedItems[]]</c>,
/// mirroring <see cref="SetDiffMessagePackFormatter{TItem}"/>.
/// </summary>
public sealed class SetDiffNerdbankConverter<TItem> : MessagePackConverter<SetDiff<TItem>>
{
    public override SetDiff<TItem> Read(ref MessagePackReader reader, SerializationContext context)
    {
        var count = reader.ReadArrayHeader();
        if (count != 2)
            throw new MessagePackSerializationException($"Expected 2 items for SetDiff<>, but got {count}.");
        var itemArrayConverter = context.GetConverter<TItem[]>(context.TypeShapeProvider);
        var added = itemArrayConverter.Read(ref reader, context)!;
        var removed = itemArrayConverter.Read(ref reader, context)!;
        return new SetDiff<TItem>(added, removed);
    }

    public override void Write(ref MessagePackWriter writer, in SetDiff<TItem> value, SerializationContext context)
    {
        writer.WriteArrayHeader(2);
        var itemArrayConverter = context.GetConverter<TItem[]>(context.TypeShapeProvider);
        itemArrayConverter.Write(ref writer, value.AddedItems, context);
        itemArrayConverter.Write(ref writer, value.RemovedItems, context);
    }
}

/// <summary>
/// Nerdbank.MessagePack converter for <see cref="SetDiff{TCollection,TItem}"/>: <c>[AddedItems[], RemovedItems[]]</c>,
/// mirroring <see cref="SetDiffMessagePackFormatter{TCollection,TItem}"/>.
/// </summary>
public sealed class SetDiffNerdbankConverter<TCollection, TItem> : MessagePackConverter<SetDiff<TCollection, TItem>>
    where TCollection : IReadOnlyCollection<TItem>
{
    public override SetDiff<TCollection, TItem> Read(ref MessagePackReader reader, SerializationContext context)
    {
        var count = reader.ReadArrayHeader();
        if (count != 2)
            throw new MessagePackSerializationException($"Expected 2 items for SetDiff<,>, but got {count}.");
        var itemArrayConverter = context.GetConverter<TItem[]>(context.TypeShapeProvider);
        var added = itemArrayConverter.Read(ref reader, context)!;
        var removed = itemArrayConverter.Read(ref reader, context)!;
        return new SetDiff<TCollection, TItem>(added, removed);
    }

    public override void Write(ref MessagePackWriter writer, in SetDiff<TCollection, TItem> value, SerializationContext context)
    {
        writer.WriteArrayHeader(2);
        var itemArrayConverter = context.GetConverter<TItem[]>(context.TypeShapeProvider);
        itemArrayConverter.Write(ref writer, value.AddedItems, context);
        itemArrayConverter.Write(ref writer, value.RemovedItems, context);
    }
}
