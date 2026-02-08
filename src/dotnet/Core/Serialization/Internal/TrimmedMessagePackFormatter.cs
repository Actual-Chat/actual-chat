using System.Numerics;
using ActualChat.Mathematics;
using MessagePack.Formatters;

namespace ActualChat.Serialization.Internal;

public sealed class TrimmedMessagePackFormatter<T> : IMessagePackFormatter<Trimmed<T>>
    where T : struct, IAdditionOperators<T, T, T>, IComparable<T>, IEquatable<T>
{
    public void Serialize(ref MessagePackWriter writer, Trimmed<T> value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(2);
        options.Resolver.GetFormatterWithVerify<T>().Serialize(ref writer, value.Value, options);
        options.Resolver.GetFormatterWithVerify<T?>().Serialize(ref writer, value.Limit, options);
    }

    public Trimmed<T> Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 2)
            throw new MessagePackSerializationException($"Expected 2 items for Trimmed<>, but got {count}.");

        var value = options.Resolver.GetFormatterWithVerify<T>().Deserialize(ref reader, options);
        var limit = options.Resolver.GetFormatterWithVerify<T?>().Deserialize(ref reader, options);
        return new Trimmed<T>(value, limit);
    }
}
