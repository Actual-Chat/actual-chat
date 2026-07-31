using MessagePack.Formatters;

namespace ActualChat.Serialization.Internal;

// Core disables the MessagePack source generator (see Core.csproj), so this hand-written
// formatter is the only one Size2D gets on AOT clients. It reproduces the [Key(N)] array
// layout DynamicObjectResolver emits on the server, so the wire format doesn't change.

/// <summary>
/// Wire-compatible array-format formatter for <see cref="Size2D"/>.
/// </summary>
public sealed class Size2DMessagePackFormatter : IMessagePackFormatter<Size2D>
{
    public void Serialize(ref MessagePackWriter writer, Size2D value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(2);
        writer.Write(value.Width);
        writer.Write(value.Height);
    }

    public Size2D Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return default;

        var count = reader.ReadArrayHeader();
        var width = 0;
        var height = 0;
        for (var i = 0; i < count; i++) {
            switch (i) {
            case 0:
                width = reader.ReadInt32();
                break;
            case 1:
                height = reader.ReadInt32();
                break;
            default:
                reader.Skip();
                break;
            }
        }

        return new Size2D(width, height);
    }
}
