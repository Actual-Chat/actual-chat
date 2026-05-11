using System.Buffers;
using MessagePack.Formatters;

namespace ActualChat.Video;

/// <summary>
/// MessagePack formatter for <see cref="VideoFrame"/> that enables serialize-once fan-out.
/// Hand-written — bypasses the auto-generated formatter to eliminate per-frame Gen0 allocation
/// of a separate <c>byte[]</c> for <see cref="VideoFrame.Data"/> and the dynamic-IL machinery
/// behind the default formatter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Serialize:</b> if <see cref="VideoFrame.SerializedData"/> is populated, the cached bytes
/// are written via <see cref="MessagePackWriter.WriteRaw(ReadOnlySpan{byte})"/> — zero-cost.
/// On cache miss the frame is encoded into a pooled <see cref="ArrayPoolBuffer{T}"/> scratch
/// buffer, the final bytes are copied to a plain <c>byte[]</c> held by the frame, the scratch
/// buffer is released back to the pool.
/// </para>
/// <para>
/// <b>Deserialize:</b> the raw MessagePack bytes for the frame are copied into a plain
/// <c>byte[]</c> owned by the frame. The frame is parsed out of that copy, so
/// <see cref="VideoFrame.Data"/> and <see cref="VideoFrame.Description"/> are slices into the
/// same <c>byte[]</c>. Lifetime is purely GC-driven: the array lives as long as any consumer
/// holds the frame. No pooling, no Dispose, no use-after-free race with lagging consumers —
/// which the new linked-list <see cref="AsyncMemoizer{T}"/> exposed: a slow consumer pins
/// nodes past the producer's head, and any pooled-buffer-returned-too-early scheme corrupts
/// their reads.
/// </para>
/// <para>
/// Bound to <see cref="VideoFrame"/> via <c>[MessagePackFormatter]</c> on the type —
/// AttributeFormatterResolver instantiates this formatter automatically.
/// MessagePack's built-in <c>ArrayFormatter&lt;VideoFrame&gt;</c> automatically delegates each
/// element to this formatter, so <see cref="VideoFrame"/>[] batches hit the cache too.
/// </para>
/// <para>
/// Wire format: an 18-entry MessagePack map with PascalCase string keys — Data (bin),
/// Offset (int64 ticks), OffsetEpoch (int32), Duration (int64 ticks), IsKeyFrame (bool),
/// Width (int32), Height (int32), Description (bin or nil), Codec (str or nil),
/// LayerId (uint8), MaxLayerId (uint8), TemporalLayerId (uint8),
/// SourceWidth (int32), SourceHeight (int32), MaxLayerWidth (int32),
/// MaxLayerHeight (int32), Index (int32), DropTrace (bin).
/// </para>
/// </remarks>
public sealed class CachingVideoFrameFormatter : IMessagePackFormatter<VideoFrame?>
{
    public static readonly CachingVideoFrameFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, VideoFrame? value, MessagePackSerializerOptions options)
    {
        if (ReferenceEquals(value, null)) {
            writer.WriteNil();
            return;
        }

        var cached = value.SerializedData;
        if (!cached.IsEmpty) {
            writer.WriteRaw(cached.Span);
            return;
        }

        EnsureSerializedData(value);
        writer.WriteRaw(value.SerializedData.Span);
    }

    public VideoFrame? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return null!;

        // Mark the start, then Skip() the entire frame map to find its end.
        var startPos = reader.Position;
        reader.Skip();
        var slice = reader.Sequence.Slice(startPos, reader.Position);
        var len = (int)slice.Length;

        // Plain GC-managed byte[] — not pooled. Exact size (no bucket rounding slack).
        // Lifetime = "as long as any VideoFrame consumer still holds a reference", handled
        // entirely by GC. Data/Description slice into this same array.
        var bytes = new byte[len];
        slice.CopyTo(bytes);
        return ParseFrame(bytes);
    }

    // --- private ---

    private static VideoFrame ParseFrame(byte[] bytes)
    {
        var reader = new MessagePackReader(bytes);
        var mapLen = reader.ReadMapHeader();

        long offsetTicks = 0, durationTicks = 0;
        var offsetEpoch = 0;
        var isKey = false;
        var width = 0;
        var height = 0;
        var sourceWidth = 0;
        var sourceHeight = 0;
        var maxLayerWidth = 0;
        var maxLayerHeight = 0;
        var index = 0;
        byte temporalLayerId = 0;
        byte layerId = 0;
        byte maxLayerId = 0;
        var dataSlice = default(ReadOnlyMemory<byte>);
        var descriptionSlice = default(ReadOnlyMemory<byte>);
        var dropTraceSlice = default(ReadOnlyMemory<byte>);
        string? codec = null;

        for (var i = 0; i < mapLen; i++) {
            var key = reader.ReadString();
            switch (key) {
                case "Data":
                    dataSlice = ReadBinSlice(ref reader);
                    break;
                case "Offset":
                    offsetTicks = reader.ReadInt64();
                    break;
                case "OffsetEpoch":
                    offsetEpoch = reader.ReadInt32();
                    break;
                case "Duration":
                    durationTicks = reader.ReadInt64();
                    break;
                case "IsKeyFrame":
                    isKey = reader.ReadBoolean();
                    break;
                case "Width":
                    width = reader.ReadInt32();
                    break;
                case "Height":
                    height = reader.ReadInt32();
                    break;
                case "Description":
                    descriptionSlice = reader.TryReadNil() ? default : ReadBinSlice(ref reader);
                    break;
                case "Codec":
                    codec = reader.TryReadNil() ? null : reader.ReadString();
                    break;
                case "LayerId":
                case "SpatialLayerId":
                    layerId = reader.ReadByte();
                    break;
                case "MinLayerId":
                case "MinSpatialLayerId":
                    reader.Skip();
                    break;
                case "MaxLayerId":
                case "MaxSpatialLayerId":
                    maxLayerId = reader.ReadByte();
                    break;
                case "TemporalLayerId":
                    temporalLayerId = reader.ReadByte();
                    break;
                case "SourceWidth":
                    sourceWidth = reader.ReadInt32();
                    break;
                case "SourceHeight":
                    sourceHeight = reader.ReadInt32();
                    break;
                case "MaxLayerWidth":
                case "MaxSpatialLayerWidth":
                    maxLayerWidth = reader.ReadInt32();
                    break;
                case "MaxLayerHeight":
                case "MaxSpatialLayerHeight":
                    maxLayerHeight = reader.ReadInt32();
                    break;
                case "Index":
                    index = reader.ReadInt32();
                    break;
                case "DropTrace":
                    dropTraceSlice = reader.TryReadNil() ? default : ReadBinSlice(ref reader);
                    break;
                default:
                    // Forward-compat: tolerate unknown fields.
                    reader.Skip();
                    break;
            }
        }

        return new VideoFrame(isKey) {
            Data = dataSlice,                       // slice of bytes
            Offset = new TimeSpan(offsetTicks),
            OffsetEpoch = offsetEpoch,
            Duration = new TimeSpan(durationTicks),
            Width = width,
            Height = height,
            Description = descriptionSlice,         // slice of bytes (may be empty)
            Codec = codec,
            LayerId = layerId,
            MaxLayerId = maxLayerId,
            TemporalLayerId = temporalLayerId,
            SourceWidth = sourceWidth,
            SourceHeight = sourceHeight,
            MaxLayerWidth = maxLayerWidth,
            MaxLayerHeight = maxLayerHeight,
            Index = index,
            DropTrace = dropTraceSlice,             // slice of bytes (may be empty)
            SerializedData = bytes,
        };
    }

    // Returns a ReadOnlyMemory that is a direct slice of the underlying single-segment buffer.
    private static ReadOnlyMemory<byte> ReadBinSlice(ref MessagePackReader reader)
    {
        var seq = reader.ReadBytes();
        return seq is { } s ? s.First : default;
    }

    private static void EnsureSerializedData(VideoFrame frame)
    {
        if (!frame.SerializedData.IsEmpty)
            return;

        // Scratch pooled buffer during serialize, final bytes live on a plain byte[].
        var scratch = new ArrayPoolBuffer<byte>(frame.Data.Length + 256, mustClear: false);
        byte[] bytes;
        try {
            var writer = new MessagePackWriter(scratch);
            WriteFrame(ref writer, frame);
            writer.Flush();
            bytes = scratch.WrittenSpan.ToArray();
        }
        finally {
            scratch.Dispose();
        }

        // Single-writer by design — SerializedData is populated at ingress (RPC receive loop).
        if (frame.SerializedData.IsEmpty)
            frame.SerializedData = bytes;
    }

    private static void WriteFrame(ref MessagePackWriter writer, VideoFrame v)
    {
        writer.WriteMapHeader(18);

        writer.Write("Data");
        writer.Write(v.Data.Span);

        writer.Write("Offset");
        writer.Write(v.Offset.Ticks);

        writer.Write("OffsetEpoch");
        writer.Write(v.OffsetEpoch);

        writer.Write("Duration");
        writer.Write(v.Duration.Ticks);

        writer.Write("IsKeyFrame");
        writer.Write(v.IsKeyFrame);

        writer.Write("Width");
        writer.Write(v.Width);

        writer.Write("Height");
        writer.Write(v.Height);

        writer.Write("Description");
        if (v.Description.IsEmpty)
            writer.WriteNil();
        else
            writer.Write(v.Description.Span);

        writer.Write("Codec");
        if (v.Codec is null)
            writer.WriteNil();
        else
            writer.Write(v.Codec);

        writer.Write("LayerId");
        writer.Write(v.LayerId);

        writer.Write("MaxLayerId");
        writer.Write(v.MaxLayerId);

        writer.Write("TemporalLayerId");
        writer.Write(v.TemporalLayerId);

        writer.Write("SourceWidth");
        writer.Write(v.SourceWidth);

        writer.Write("SourceHeight");
        writer.Write(v.SourceHeight);

        writer.Write("MaxLayerWidth");
        writer.Write(v.MaxLayerWidth);

        writer.Write("MaxLayerHeight");
        writer.Write(v.MaxLayerHeight);

        writer.Write("Index");
        writer.Write(v.Index);

        writer.Write("DropTrace");
        if (v.DropTrace.IsEmpty)
            writer.Write(ReadOnlySpan<byte>.Empty);
        else
            writer.Write(v.DropTrace.Span);
    }
}
