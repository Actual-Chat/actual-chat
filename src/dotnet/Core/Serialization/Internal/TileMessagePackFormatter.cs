using System.Numerics;
using MessagePack.Formatters;

namespace ActualChat.Serialization.Internal;

/// <summary>
/// Array-format formatter for <see cref="Tile{T}"/>; the deserialized tile is
/// unbound, i.e. its <see cref="Tile{T}.Layer"/> is null.
/// </summary>
public sealed class TileMessagePackFormatter<T> : IMessagePackFormatter<Tile<T>>
    where T : struct, INumber<T>
{
    public void Serialize(ref MessagePackWriter writer, Tile<T> value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(1);
        options.Resolver.GetFormatterWithVerify<Range<T>>().Serialize(ref writer, value.Range, options);
    }

    public Tile<T> Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 1)
            throw new MessagePackSerializationException($"Expected 1 item for Tile<>, but got {count}.");

        var range = options.Resolver.GetFormatterWithVerify<Range<T>>().Deserialize(ref reader, options);
        return new Tile<T>(range);
    }
}
