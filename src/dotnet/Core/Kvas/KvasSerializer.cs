using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Stl.Serialization.Internal;

namespace ActualChat.Kvas;

#pragma warning disable IL2026, IL2046, IL2092 // We change everything to DynamicallyAccessedMemberTypes.All here

public class KvasSerializer : ByteSerializerBase
{
    private const byte ByteFormatMarker = 0x0;
    private static readonly byte[] ByteFormatHeader = { ByteFormatMarker };

    public static KvasSerializer Default { get; set; } = new();
    public static readonly byte[] SerializedTrue = Default.Write(true).WrittenMemory.ToArray();

    public IByteSerializer ByteSerializer { get; init; } = MemoryPackByteSerializer.Default;
    public ITextSerializer TextSerializer { get; init; } = SystemJsonSerializer.Default;

    public override object? Read(
        ReadOnlyMemory<byte> data,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type,
        out int readLength)
    {
        var isText = data.Length == 0 || data.Span[0] != ByteFormatMarker;
        var result = isText
            ? TextSerializer.Read(data, type, out readLength)
            : ByteSerializer.Read(data[1..], type, out readLength);
        return result;
    }

    public override void Write(
        IBufferWriter<byte> bufferWriter,
        object? value,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        bufferWriter.Write(ByteFormatHeader);
        ByteSerializer.Write(bufferWriter, value, type);
    }
}
