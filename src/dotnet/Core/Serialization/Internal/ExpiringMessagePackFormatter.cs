using MessagePack;
using MessagePack.Formatters;

namespace ActualChat.Serialization.Internal;

public sealed class ExpiringMessagePackFormatter<T> : IMessagePackFormatter<Expiring<T>?>
{
    public void Serialize(ref MessagePackWriter writer, Expiring<T>? value, MessagePackSerializerOptions options)
    {
        if (value is null) {
            writer.WriteNil();
            return;
        }
        writer.WriteArrayHeader(2);
        options.Resolver.GetFormatterWithVerify<T>().Serialize(ref writer, value.Value, options);
        options.Resolver.GetFormatterWithVerify<Moment>().Serialize(ref writer, value.ExpiresAt, options);
    }

    public Expiring<T>? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return null;
        var count = reader.ReadArrayHeader();
        if (count != 2)
            throw new MessagePackSerializationException($"Expected 2 items for Expiring<>, but got {count}.");

        var value = options.Resolver.GetFormatterWithVerify<T>().Deserialize(ref reader, options);
        var expiresAt = options.Resolver.GetFormatterWithVerify<Moment>().Deserialize(ref reader, options);
        return new Expiring<T>(value, expiresAt);
    }
}
