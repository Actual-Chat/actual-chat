using System.Buffers;
using ActualLab.Serialization.Internal;

namespace ActualChat.Kvas;

/// <summary>
/// Serializer for <see cref="IKvas"/> that supports both binary and text formats.
/// </summary>
public class KvasSerializer : ByteSerializerBase
{
    private const byte MemoryPackMarker = 0x0;
    private const byte MessagePackMarker = 0x1;
    private static readonly byte[] MemoryPackHeader = [MemoryPackMarker];
    private static readonly byte[] MessagePackHeader = [MessagePackMarker];

    private ILogger Log { get; } = StaticLog.For<KvasSerializer>();

    public static KvasSerializer Default { get; set; } = new();
    public static readonly byte[] SerializedTrue = Default.Write(true, typeof(bool)).WrittenMemory.ToArray();

    public bool PreferMemoryPack { get; init; }
    public IByteSerializer MemoryPackSerializer { get; init; } = Serializers.MemoryPack;
    public IByteSerializer MessagePackSerializer { get; init; } = Serializers.MessagePack;
    public ITextSerializer TextSerializer { get; init; } = Serializers.SystemJson;

    public override object? Read(
        ReadOnlyMemory<byte> data,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type,
        out int readLength)
    {
        try {
            if (data.Length == 0) {
                return TextSerializer.Read(data, type, out readLength);
            }
            var marker = data.Span[0];
            return marker switch {
                MemoryPackMarker => MemoryPackSerializer.Read(data[1..], type, out readLength),
                MessagePackMarker => MessagePackSerializer.Read(data[1..], type, out readLength),
                _ => TextSerializer.Read(data, type, out readLength), // Legacy JSON format (no marker)
            };
        }
        catch (Exception e) when (e is MemoryPackSerializationException
            or MessagePackSerializationException
            or System.Text.Json.JsonException) {
            Log.LogWarning(e, "Failed to deserialize data of type {Type} with length {Length}", type, data.Length);
            readLength = 0;
            return null;
        }
    }

    public override void Write(
        IBufferWriter<byte> bufferWriter,
        object? value,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        if (PreferMemoryPack) {
            bufferWriter.Write(MemoryPackHeader);
            MemoryPackSerializer.Write(bufferWriter, value, type);
        }
        else {
            bufferWriter.Write(MessagePackHeader);
            MessagePackSerializer.Write(bufferWriter, value, type);
        }
    }
}
