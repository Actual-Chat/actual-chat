using System.Buffers;
using Stl.Serialization.Internal;

namespace ActualChat.Kvas;

public class KvasSerializer : ByteSerializerBase
{
    private const byte ByteFormatMarker = 0x0;
    private static readonly byte[] ByteFormatHeader = { ByteFormatMarker };

    public static IByteSerializer Default { get; set; } = new KvasSerializer();
#pragma warning disable IL2026
    public static readonly byte[] SerializedTrue = Default.Write(true).WrittenMemory.ToArray();

    public IByteSerializer ByteSerializer { get; init; } = MemoryPackByteSerializer.Default;
    public ITextSerializer TextSerializer { get; init; } = SystemJsonSerializer.Default;

    public override object? Read(ReadOnlyMemory<byte> data, Type type, out int readLength)
    {
        var isText = data.Length == 0 || data.Span[0] != ByteFormatMarker;
        var result = isText
            ? TextSerializer.Read(data, type, out readLength)
            : ByteSerializer.Read(data[1..], type, out readLength);
        return result;
    }

    public override void Write(IBufferWriter<byte> bufferWriter, object? value, Type type)
    {
        bufferWriter.Write(ByteFormatHeader);
        ByteSerializer.Write(bufferWriter, value, type);
    }
}
