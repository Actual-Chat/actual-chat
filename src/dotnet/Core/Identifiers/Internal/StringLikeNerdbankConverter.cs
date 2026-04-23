
namespace ActualChat.Internal;

/// <summary>
/// Nerdbank.MessagePack converter for <see cref="IStringLike{T}"/> types — both classes and
/// structs. Serializes as the raw <see cref="IStringLike.Value"/> string, deserializes via
/// <see cref="IStringLike{TSelf}.Parse(string?)"/>. Works for reference-type identifiers
/// (null-valued → nil) and value-type identifiers (default-valued still produces a string).
/// </summary>
public sealed class StringLikeNerdbankConverter<T> : MessagePackConverter<T>
    where T : IStringLike<T>
{
    // Wire shape:
    //   reference type, value == null   → nil
    //   value-type, default, Value null → nil (same as reference)
    //   otherwise                       → plain msgpack string of Value
    public override T Read(ref MessagePackReader reader, SerializationContext context)
    {
        if (reader.TryReadNil())
            return default!;
        var s = reader.ReadString();
        return string.IsNullOrEmpty(s) ? default! : T.Parse(s);
    }

    public override void Write(ref MessagePackWriter writer, in T? value, SerializationContext context)
    {
        // null for ref types; default for struct types where Value resolves to null.
        if (value?.Value is null)
            writer.WriteNil();
        else
            writer.Write(value.Value);
    }
}
