using MessagePack.Formatters;

namespace ActualChat.Serialization.Internal;

/// <summary>
/// Reads nil as an empty string.
/// For a non-nullable string member added at a key that older array-form payloads hold as nil.
/// </summary>
public sealed class NilTolerantStringMessagePackFormatter : IMessagePackFormatter<string>
{
    public void Serialize(ref MessagePackWriter writer, string value, MessagePackSerializerOptions options)
        => writer.Write(value);

    public string Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        => reader.ReadString() ?? "";
}
