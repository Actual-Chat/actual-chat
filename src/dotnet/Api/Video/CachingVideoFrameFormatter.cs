using System.Buffers;
using ActualLab.Collections;
using MessagePack;
using MessagePack.Formatters;

namespace ActualChat.Video;

internal static class VideoFramePools
{
    // ArrayPool<byte> for VideoFrame.SerializedData buffers.
    //
    // Uses ArrayPool<byte>.Shared. Tried a dedicated ConfigurableArrayPool with
    // maxArraysPerBucket=128 to cap memory footprint; that caused:
    //  - p99 latency spikes to 386 ms (was 50 ms)
    //  - 1.4% frame drops
    //  - 6× more Gen2 GCs (DestroyScout.Finalize CPU 4.6 s → 27.5 s)
    // because the 128-array cap was far below the working set (~9000 concurrent
    // retained frames at 30 fps × 60 producer streams × 5 s retention). Arrays
    // overflowed the bucket every burst and were reallocated each rent.
    //
    // SharedArrayPool uses per-thread + per-core strong-ref buckets plus weak-ref
    // overflow, which tracks bursty working sets well and trims under GC pressure.
    // The ~500 MB apparent post-test retention is normal pool behavior, not a leak —
    // in production those arrays get reused across continuous sessions.
    public static readonly ArrayPool<byte> BytePool = ArrayPool<byte>.Shared;
}

/// <summary>
/// MessagePack formatter for <see cref="VideoFrame"/> that enables serialize-once fan-out.
/// Hand-written — bypasses the auto-generated formatter to eliminate per-frame Gen0 allocation
/// (the inner formatter's separate <c>byte[]</c> for <see cref="VideoFrame.Data"/>) and the
/// dynamic-IL machinery behind it (<c>DynamicResolver+DestroyScout.Finalize</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Serialize:</b> if <see cref="VideoFrame.SerializedData"/> is populated, the cached bytes are
/// written via <see cref="MessagePackWriter.WriteRaw(ReadOnlySpan{byte})"/> (zero serialization).
/// On cache miss the frame is encoded into a pooled <see cref="ArrayPoolBuffer{T}"/>, the result
/// is cached on the frame, and then written raw.
/// </para>
/// <para>
/// <b>Deserialize:</b> the raw MessagePack bytes for the frame are copied into one pooled
/// <see cref="ArrayPoolBuffer{T}"/>. The frame is then parsed out of that copy, so
/// <see cref="VideoFrame.Data"/> and <see cref="VideoFrame.Description"/> are direct slices into
/// the same pooled buffer — no separately-allocated <c>byte[]</c> for the payload.
/// </para>
/// <para>
/// Scoped to <see cref="VideoFrame"/> only via <see cref="CachingVideoFrameResolver"/>.
/// MessagePack's built-in <c>ArrayFormatter&lt;VideoFrame&gt;</c> automatically delegates each
/// element to this formatter, so <see cref="VideoFrame"/>[] batches hit the cache too.
/// </para>
/// <para>
/// Wire format (must match what the default source-gen formatter would produce): a
/// 9-entry MessagePack map with PascalCase string keys — Data (bin), Offset (int64 ticks),
/// Duration (int64 ticks), IsKeyFrame (bool), Width (int32), Height (int32),
/// Description (bin or nil), Codec (str or nil), TemporalLayerId (int32).
/// </para>
/// </remarks>
public sealed class CachingVideoFrameFormatter : IMessagePackFormatter<VideoFrame>
{
    public static readonly CachingVideoFrameFormatter Instance = new();

    private CachingVideoFrameFormatter()
    { }

    public void Serialize(ref MessagePackWriter writer, VideoFrame value, MessagePackSerializerOptions options)
    {
        if (value is null) {
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

    public VideoFrame Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return null!;

        // Mark the start, then Skip() the entire frame map to find its end.
        var startPos = reader.Position;
        reader.Skip();
        var slice = reader.Sequence.Slice(startPos, reader.Position);
        var len = (int)slice.Length;

        // One pooled buffer holds the raw MessagePack bytes; Data/Description slice into it.
        // Dedicated BytePool avoids polluting SharedArrayPool with ~500 MB of video-only buffers.
        var buffer = new ArrayPoolBuffer<byte>(VideoFramePools.BytePool, len, mustClear: false);
        slice.CopyTo(buffer.GetSpan(len));
        buffer.Advance(len);

        try {
            return ParseFrame(buffer);
        }
        catch {
            buffer.Dispose();
            throw;
        }
    }

    // --- private ---

    private static VideoFrame ParseFrame(ArrayPoolBuffer<byte> buffer)
    {
        var copyReader = new MessagePackReader(buffer.WrittenMemory);
        var mapLen = copyReader.ReadMapHeader();

        long offsetTicks = 0, durationTicks = 0;
        var isKey = false;
        var width = 0;
        var height = 0;
        var temporalLayerId = 0;
        var dataSlice = default(ReadOnlyMemory<byte>);
        var descriptionSlice = default(ReadOnlyMemory<byte>);
        string? codec = null;

        for (var i = 0; i < mapLen; i++) {
            var key = copyReader.ReadString();
            switch (key) {
                case "Data":
                    dataSlice = ReadBinSlice(ref copyReader);
                    break;
                case "Offset":
                    offsetTicks = copyReader.ReadInt64();
                    break;
                case "Duration":
                    durationTicks = copyReader.ReadInt64();
                    break;
                case "IsKeyFrame":
                    isKey = copyReader.ReadBoolean();
                    break;
                case "Width":
                    width = copyReader.ReadInt32();
                    break;
                case "Height":
                    height = copyReader.ReadInt32();
                    break;
                case "Description":
                    descriptionSlice = copyReader.TryReadNil() ? default : ReadBinSlice(ref copyReader);
                    break;
                case "Codec":
                    codec = copyReader.TryReadNil() ? null : copyReader.ReadString();
                    break;
                case "TemporalLayerId":
                    temporalLayerId = copyReader.ReadInt32();
                    break;
                default:
                    // Forward-compat: tolerate unknown fields.
                    copyReader.Skip();
                    break;
            }
        }

        return new VideoFrame(isKey) {
            Data = dataSlice,                       // slice of pooled buffer
            Offset = new TimeSpan(offsetTicks),
            Duration = new TimeSpan(durationTicks),
            Width = width,
            Height = height,
            Description = descriptionSlice,         // slice of pooled buffer (may be empty)
            Codec = codec,
            TemporalLayerId = temporalLayerId,
            SerializedDataOwner = buffer,
            SerializedData = buffer.WrittenMemory,
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

        var pooled = new ArrayPoolBuffer<byte>(
            VideoFramePools.BytePool, frame.Data.Length + 256, mustClear: false);
        try {
            var scratch = new MessagePackWriter(pooled);
            WriteFrame(ref scratch, frame);
            scratch.Flush();
        }
        catch {
            pooled.Dispose();
            throw;
        }

        // First writer wins — if another thread raced us, discard ours.
        if (frame.SerializedData.IsEmpty) {
            frame.SerializedDataOwner = pooled;
            frame.SerializedData = pooled.WrittenMemory;
        }
        else
            pooled.Dispose();
    }

    private static void WriteFrame(ref MessagePackWriter writer, VideoFrame v)
    {
        writer.WriteMapHeader(9);

        writer.Write("Data");
        writer.Write(v.Data.Span);

        writer.Write("Offset");
        writer.Write(v.Offset.Ticks);

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

        writer.Write("TemporalLayerId");
        writer.Write(v.TemporalLayerId);
    }
}
