using MessagePack.Formatters;

namespace ActualChat.Serialization.Internal;

public sealed class SetDiffMessagePackFormatter<TItem> : IMessagePackFormatter<SetDiff<TItem>>
{
    public void Serialize(ref MessagePackWriter writer, SetDiff<TItem> value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(2);
        options.Resolver.GetFormatterWithVerify<TItem[]>().Serialize(ref writer, value.AddedItems, options);
        options.Resolver.GetFormatterWithVerify<TItem[]>().Serialize(ref writer, value.RemovedItems, options);
    }

    public SetDiff<TItem> Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 2)
            throw new MessagePackSerializationException($"Expected 2 items for SetDiff<>, but got {count}.");

        var addedItems = options.Resolver.GetFormatterWithVerify<TItem[]>().Deserialize(ref reader, options);
        var removedItems = options.Resolver.GetFormatterWithVerify<TItem[]>().Deserialize(ref reader, options);
        return new SetDiff<TItem>(addedItems, removedItems);
    }
}

public sealed class SetDiffMessagePackFormatter<TCollection, TItem> : IMessagePackFormatter<SetDiff<TCollection, TItem>>
    where TCollection : IReadOnlyCollection<TItem>
{
    public void Serialize(ref MessagePackWriter writer, SetDiff<TCollection, TItem> value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(2);
        options.Resolver.GetFormatterWithVerify<TItem[]>().Serialize(ref writer, value.AddedItems, options);
        options.Resolver.GetFormatterWithVerify<TItem[]>().Serialize(ref writer, value.RemovedItems, options);
    }

    public SetDiff<TCollection, TItem> Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 2)
            throw new MessagePackSerializationException($"Expected 2 items for SetDiff<,>, but got {count}.");

        var addedItems = options.Resolver.GetFormatterWithVerify<TItem[]>().Deserialize(ref reader, options);
        var removedItems = options.Resolver.GetFormatterWithVerify<TItem[]>().Deserialize(ref reader, options);
        return new SetDiff<TCollection, TItem>(addedItems, removedItems);
    }
}
