using System.Buffers;
using ActualLab.Serialization.Internal;

namespace ActualChat.Serialization;

/// <summary>
/// An <see cref="IByteSerializer"/> that prefixes each serialized payload with a 1-byte format
/// version. On read, the first byte selects which serializer in <see cref="Serializers"/> to use;
/// if the byte is outside the array bounds, or if deserialization with the matched serializer
/// fails, the <see cref="Legacy"/> serializer is used as a fallback (when provided) on the full
/// (un-stripped) data. On write, the last serializer in <see cref="Serializers"/> is used and
/// its index is written as the version byte.
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCodeAttribute",
    Justification = "The wrapped serializers carry their own DynamicallyAccessedMembers requirements.")]
public sealed class VersionedByteSerializer : ByteSerializerBase
{
    public IByteSerializer[] Serializers { get; }
    public IByteSerializer? Legacy { get; }
    public byte CurrentVersion { get; }

    public VersionedByteSerializer(IByteSerializer[] serializers, IByteSerializer? legacy = null)
    {
        ArgumentNullException.ThrowIfNull(serializers);
        if (serializers.Length == 0)
            throw new ArgumentException("At least one serializer is required.", nameof(serializers));
        if (serializers.Length > 256)
            throw new ArgumentException("At most 256 serializers are supported.", nameof(serializers));

        Serializers = serializers;
        Legacy = legacy;
        CurrentVersion = (byte)(serializers.Length - 1);
    }

    public override object? Read(ReadOnlyMemory<byte> data, Type type, out int readLength)
    {
        if (data.Length == 0)
            return Legacy is not null
                ? Legacy.Read(data, type, out readLength)
                : throw StandardError.Internal(
                    "Cannot deserialize: data is empty and no legacy serializer is provided.");

        var version = data.Span[0];
        if (version >= Serializers.Length)
            return Legacy is not null
                ? Legacy.Read(data, type, out readLength)
                : throw StandardError.Internal(
                    $"Cannot deserialize: format version byte 0x{version:X2} is out of range "
                    + $"(max version: 0x{CurrentVersion:X2}) and no legacy serializer is provided.");

        // Version byte is in range -- try the matching serializer first.
        if (Legacy is null) {
            var result = Serializers[version].Read(data[1..], type, out var rl);
            readLength = rl + 1;
            return result;
        }

        Exception error;
        try {
            var result = Serializers[version].Read(data[1..], type, out var rl);
            readLength = rl + 1;
            return result;
        }
        catch (Exception e) {
            error = e;
        }

        try {
            return Legacy.Read(data, type, out readLength);
        }
        catch {
            throw error;
        }
    }

    public override void Write(IBufferWriter<byte> bufferWriter, object? value, Type type)
    {
        var span = bufferWriter.GetSpan(1);
        span[0] = CurrentVersion;
        bufferWriter.Advance(1);
        Serializers[CurrentVersion].Write(bufferWriter, value, type);
    }
}
