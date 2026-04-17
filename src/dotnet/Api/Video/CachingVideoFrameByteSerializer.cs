using System.Buffers;
using ActualLab.Collections;
using ActualLab.Serialization;
using ActualLab.Serialization.Internal;

namespace ActualChat.Video;

/// <summary>
/// Wraps an <see cref="IByteSerializer"/> to cache serialized bytes on <see cref="VideoFrame"/>
/// for serialize-once fan-out. When the same frame is serialized for multiple RPC consumers,
/// the first call serializes into a pooled <see cref="ArrayPoolBuffer{T}"/> and caches on the frame;
/// subsequent calls write directly from the cached buffer — zero allocation.
/// The pooled buffer is returned to ArrayPool when the frame is disposed.
/// </summary>
public sealed class CachingVideoFrameByteSerializer(IByteSerializer baseSerializer) : ByteSerializerBase
{
    public override object? Read(ReadOnlyMemory<byte> data, Type type, out int readLength)
    {
        var result = baseSerializer.Read(data, type, out readLength);
        // Capture raw serialized bytes on ingest so fan-out Write() calls get cache HIT (zero serialization)
        if (type == typeof(VideoFrame) && result is VideoFrame frame && frame.SerializedData.IsEmpty) {
            var buffer = new ArrayPoolBuffer<byte>(readLength, mustClear: false);
            data.Span.Slice(0, readLength).CopyTo(buffer.GetSpan(readLength));
            buffer.Advance(readLength);
            frame.SerializedDataOwner = buffer;
            frame.SerializedData = buffer.WrittenMemory;
        }
        return result;
    }

    public override void Write(IBufferWriter<byte> bufferWriter, object? value, Type type)
    {
        if (type == typeof(VideoFrame) && value is VideoFrame frame) {
            WriteVideoFrame(bufferWriter, frame);
            return;
        }

        baseSerializer.Write(bufferWriter, value, type);
    }

    public override IByteSerializer<T> ToTyped<T>(Type? serializedType = null)
        => new CastingByteSerializer<T>(this, serializedType ?? typeof(T));

    private void WriteVideoFrame(IBufferWriter<byte> bufferWriter, VideoFrame frame)
    {
        // Cache hit — write pre-serialized bytes directly
        var cached = frame.SerializedData;
        if (!cached.IsEmpty) {
            bufferWriter.Write(cached.Span);
            return;
        }

        // Cache miss — serialize into pooled ArrayPoolBuffer, cache on frame.
        // ArrayPoolBuffer rents from ArrayPool and returns on Dispose.
        // Frame.Dispose() will dispose the buffer, returning the array to pool.
        var pooledBuffer = new ArrayPoolBuffer<byte>(frame.Data.Length + 256, mustClear: false);
        try {
            baseSerializer.Write(pooledBuffer, frame, typeof(VideoFrame));

            // First writer wins — if another thread raced us, discard ours
            if (frame.SerializedData.IsEmpty) {
                frame.SerializedDataOwner = pooledBuffer;
                frame.SerializedData = pooledBuffer.WrittenMemory;
            }
            else
                // Another thread won the race — dispose our buffer, use theirs
                pooledBuffer.Dispose();

            bufferWriter.Write(frame.SerializedData.Span);
        }
        catch {
            pooledBuffer.Dispose();
            throw;
        }
    }
}
