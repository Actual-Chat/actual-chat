using System.Buffers;
using ActualLab.Serialization.Internal;
using MemoryPack;

namespace ActualChat.Kvas;

#pragma warning disable IL2026, IL2046, IL2092 // We change everything to DynamicallyAccessedMemberTypes.All here

[UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCodeAttribute", Justification = "Type is already marked with DynamicallyAccessedMembers.")]
public class KvasSerializer : ByteSerializerBase
{
    private const byte ByteFormatMarker = 0x0;
    private static readonly byte[] ByteFormatHeader = { ByteFormatMarker };

    private ILogger Log { get; } = StaticLog.For<KvasSerializer>();

    public static KvasSerializer Default { get; set; } = new();
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCodeAttribute", Justification = "True value always can be serialized.")]
    public static readonly byte[] SerializedTrue = Default.Write(true, typeof(bool)).WrittenMemory.ToArray();

    public IByteSerializer ByteSerializer { get; init; } = MemoryPackByteSerializer.Default;
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
            var isText = data.Length == 0 || data.Span[0] != ByteFormatMarker;
            var result = isText
                ? TextSerializer.Read(data, type, out readLength)
                : ByteSerializer.Read(data[1..], type, out readLength);
            return result;
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
        bufferWriter.Write(ByteFormatHeader);
        ByteSerializer.Write(bufferWriter, value, type);
    }
}
