using System.Collections.Immutable;

namespace ActualChat.Serialization.Internal;

/// <summary>
/// Nerdbank.MessagePack converter for <see cref="IImmutableDictionary{TKey,TValue}"/>.
/// Wire format mirrors <see cref="ApiMapNerdbankConverter{TKey,TValue}"/>: a plain msgpack
/// map <c>{k1: v1, k2: v2, ...}</c>. Reads are tolerant of the legacy
/// <c>[[k, v], [k, v], ...]</c> array-of-pairs shape that MessagePack-CSharp's source-gen
/// formatter wrote for IDictionary types — KVAS blobs persisted before the migration still
/// round-trip.
/// </summary>
public sealed class ImmutableDictionaryNerdbankConverter<TKey, TValue> : MessagePackConverter<IImmutableDictionary<TKey, TValue>?>
    where TKey : notnull
{
    public override IImmutableDictionary<TKey, TValue>? Read(ref MessagePackReader reader, SerializationContext context)
    {
        if (reader.TryReadNil())
            return null;

        var keyConverter = context.GetConverter<TKey>(context.TypeShapeProvider);
        var valueConverter = context.GetConverter<TValue>(context.TypeShapeProvider);
        var builder = ImmutableDictionary.CreateBuilder<TKey, TValue>();

        if (reader.NextMessagePackType == MessagePackType.Array) {
            var count = reader.ReadArrayHeader();
            for (var i = 0; i < count; i++) {
                var pairLen = reader.ReadArrayHeader();
                if (pairLen != 2)
                    throw new MessagePackSerializationException(
                        $"Expected 2-element kv pair inside IImmutableDictionary<,>, got {pairLen}.");
                var k = keyConverter.Read(ref reader, context)!;
                var v = valueConverter.Read(ref reader, context)!;
                builder[k] = v;
            }
        }
        else {
            var count = reader.ReadMapHeader();
            for (var i = 0; i < count; i++) {
                var k = keyConverter.Read(ref reader, context)!;
                var v = valueConverter.Read(ref reader, context)!;
                builder[k] = v;
            }
        }
        return builder.ToImmutable();
    }

    public override void Write(ref MessagePackWriter writer, in IImmutableDictionary<TKey, TValue>? value, SerializationContext context)
    {
        if (value is null) {
            writer.WriteNil();
            return;
        }
        writer.WriteMapHeader(value.Count);
        if (value.Count == 0)
            return;
        var keyConverter = context.GetConverter<TKey>(context.TypeShapeProvider);
        var valueConverter = context.GetConverter<TValue>(context.TypeShapeProvider);
        foreach (var kvp in value) {
            keyConverter.Write(ref writer, kvp.Key, context);
            valueConverter.Write(ref writer, kvp.Value, context);
        }
    }
}
