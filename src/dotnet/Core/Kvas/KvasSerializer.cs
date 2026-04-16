using System.Buffers;
using ActualLab.Serialization.Internal;

namespace ActualChat.Kvas;

/// <summary>
/// Serializer for <see cref="IKvas"/> that supports both binary and text formats.
/// </summary>
#pragma warning disable IL2026, IL2046, IL2092 // We change everything to DynamicallyAccessedMemberTypes.All here

[UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCodeAttribute", Justification = "Type is already marked with DynamicallyAccessedMembers.")]
public class KvasSerializer : ByteSerializerBase
{
    private const byte MemoryPackMarker = 0x0;
    private const byte MessagePackMarker = 0x1;
    private static readonly byte[] MemoryPackHeader = [MemoryPackMarker];
    private static readonly byte[] MessagePackHeader = [MessagePackMarker];

    private ILogger Log { get; } = StaticLog.For<KvasSerializer>();

    public static KvasSerializer Default { get; set; } = new();
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCodeAttribute", Justification = "True value always can be serialized.")]
    public static readonly byte[] SerializedTrue = Default.Write(true, typeof(bool)).WrittenMemory.ToArray();

    public IByteSerializer MemoryPackSerializer { get; init; } = MemoryPackByteSerializer.Default;
    public IByteSerializer MessagePackSerializer { get; init; } = MessagePackByteSerializer.Default;
    public ITextSerializer TextSerializer { get; init; } = SystemJsonSerializer.Default;

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCodeAttribute", Justification = "Type is already marked with DynamicallyAccessedMembers.")]
    [UnconditionalSuppressMessage("Trimming", "IL2092:RequiresUnreferencedCodeAttribute", Justification = "Type is already marked with DynamicallyAccessedMembers.")]
    [UnconditionalSuppressMessage("Trimming", "IL2046:RequiresUnreferencedCodeAttribute", Justification = "Type is already marked with DynamicallyAccessedMembers.")]
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
        catch (MemoryPackSerializationException e) {
            Log.LogWarning(e, "Failed to deserialize data of type {Type} with length {Length}", type, data.Length);
            readLength = 0;
            return null;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCodeAttribute", Justification = "Type is already marked with DynamicallyAccessedMembers.")]
    [UnconditionalSuppressMessage("Trimming", "IL2046:RequiresUnreferencedCodeAttribute", Justification = "Type is already marked with DynamicallyAccessedMembers.")]
    [UnconditionalSuppressMessage("Trimming", "IL2092:RequiresUnreferencedCodeAttribute", Justification = "Type is already marked with DynamicallyAccessedMembers.")]
    public override void Write(
        IBufferWriter<byte> bufferWriter,
        object? value,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        bufferWriter.Write(MessagePackHeader);
        MessagePackSerializer.Write(bufferWriter, value, type);
    }
}
