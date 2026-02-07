using MessagePack;
using MessagePack.Formatters;

namespace ActualChat.Serialization.Internal;

public sealed class ChangeMessagePackFormatter<TCreate, TUpdate> : IMessagePackFormatter<Change<TCreate, TUpdate>>
{
    public void Serialize(ref MessagePackWriter writer, Change<TCreate, TUpdate>? value, MessagePackSerializerOptions options)
    {
        if (value is null) {
            writer.WriteNil();
            return;
        }
        writer.WriteArrayHeader(3);
        options.Resolver.GetFormatterWithVerify<Option<TCreate>>().Serialize(ref writer, value.Create, options);
        options.Resolver.GetFormatterWithVerify<Option<TUpdate>>().Serialize(ref writer, value.Update, options);
        writer.Write(value.Remove);
    }

    public Change<TCreate, TUpdate>? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return null;
        var count = reader.ReadArrayHeader();
        if (count != 3)
            throw new MessagePackSerializationException($"Expected 3 items for Change<,>, but got {count}.");

        var create = options.Resolver.GetFormatterWithVerify<Option<TCreate>>().Deserialize(ref reader, options);
        var update = options.Resolver.GetFormatterWithVerify<Option<TUpdate>>().Deserialize(ref reader, options);
        var remove = reader.ReadBoolean();
        return new Change<TCreate, TUpdate> { Create = create, Update = update, Remove = remove };
    }
}

public sealed class ChangeMessagePackFormatter<T> : IMessagePackFormatter<Change<T>>
{
    private readonly ChangeMessagePackFormatter<T, T> _inner = new();

    public void Serialize(ref MessagePackWriter writer, Change<T>? value, MessagePackSerializerOptions options)
        => _inner.Serialize(ref writer, value, options);

    public Change<T>? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var result = _inner.Deserialize(ref reader, options);
        return result is null ? null : new Change<T> { Create = result.Create, Update = result.Update, Remove = result.Remove };
    }
}
