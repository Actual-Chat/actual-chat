using MessagePack.Formatters;

namespace ActualChat.Serialization.Internal;

/// <summary>
/// Array-format formatter for <see cref="Maybe{T}"/>: <c>[HasValue, Value]</c>,
/// where the second slot is nil for <see cref="Maybe.None{T}"/>.
/// </summary>
public sealed class MaybeMessagePackFormatter<T> : IMessagePackFormatter<Maybe<T>?>
{
    public void Serialize(ref MessagePackWriter writer, Maybe<T>? value, MessagePackSerializerOptions options)
    {
        if (value is null) {
            writer.WriteNil();
            return;
        }

        writer.WriteArrayHeader(2);
        writer.Write(value.HasValue);
        if (value.HasValue)
            options.Resolver.GetFormatterWithVerify<T>().Serialize(ref writer, value.ValueOrDefault!, options);
        else
            writer.WriteNil();
    }

    public Maybe<T>? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return null;

        var count = reader.ReadArrayHeader();
        if (count != 2)
            throw new MessagePackSerializationException($"Expected 2 items for Maybe<>, but got {count}.");

        var hasValue = reader.ReadBoolean();
        if (!hasValue) {
            reader.Skip();
            return Maybe.None<T>();
        }

        var value = options.Resolver.GetFormatterWithVerify<T>().Deserialize(ref reader, options);
        return Maybe.Value(value);
    }
}
