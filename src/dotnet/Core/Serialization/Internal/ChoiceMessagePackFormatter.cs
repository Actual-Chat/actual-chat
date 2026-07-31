using MessagePack.Formatters;

namespace ActualChat.Serialization.Internal;

/// <summary>
/// Array-format formatter for <see cref="Choice{T,TAlt}"/>: <c>[HasValue, Value, Alternative]</c>,
/// where the slot of the branch that isn't taken is nil.
/// </summary>
public sealed class ChoiceMessagePackFormatter<T, TAlt> : IMessagePackFormatter<Choice<T, TAlt>?>
{
    public void Serialize(ref MessagePackWriter writer, Choice<T, TAlt>? value, MessagePackSerializerOptions options)
    {
        if (value is null) {
            writer.WriteNil();
            return;
        }

        writer.WriteArrayHeader(3);
        writer.Write(value.HasValue);
        if (value.HasValue) {
            options.Resolver.GetFormatterWithVerify<T>().Serialize(ref writer, value.ValueOrDefault!, options);
            writer.WriteNil();
        }
        else {
            writer.WriteNil();
            options.Resolver.GetFormatterWithVerify<TAlt>().Serialize(ref writer, value.AlternativeOrDefault!, options);
        }
    }

    public Choice<T, TAlt>? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return null;

        var count = reader.ReadArrayHeader();
        if (count != 3)
            throw new MessagePackSerializationException($"Expected 3 items for Choice<,>, but got {count}.");

        var hasValue = reader.ReadBoolean();
        if (hasValue) {
            var value = options.Resolver.GetFormatterWithVerify<T>().Deserialize(ref reader, options);
            reader.Skip();
            return Choice.Value<T, TAlt>(value);
        }

        reader.Skip();
        var alternative = options.Resolver.GetFormatterWithVerify<TAlt>().Deserialize(ref reader, options);
        return Choice.Alternative<T, TAlt>(alternative);
    }
}
