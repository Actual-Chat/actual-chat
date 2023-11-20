using System.Buffers;
using System.Buffers.Binary;

namespace ActualChat.Audio;

public class ActualOpusStreamConverter(MomentClockSet clocks, ILogger log) : IAudioStreamConverter
{
    private MomentClockSet Clocks { get; } = clocks;
    private ILogger Log { get; } = log;

    public int FramesPerChunk { get; init; } = 3;

    public async Task<AudioSource> FromByteStream(
        IAsyncEnumerable<byte[]> byteStream,
        CancellationToken cancellationToken = default)
    {
        var headerSource = TaskCompletionSourceExt.New<ActualOpusStreamHeader>();
        var headerTask = headerSource.Task;

        // We're doing this fairly complex processing via tasks & channels only
        // because "async IAsyncEnumerable<..>" methods can't contain
        // "yield return" inside "catch" blocks, and we need this here.
        var target = Channel.CreateBounded<AudioFrame>(
            new BoundedChannelOptions(Constants.Queues.OpusStreamConverterQueueSize) {
                SingleWriter = true,
                SingleReader = true,
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.Wait,
            });

        var _ = BackgroundTask.Run(async () => {
            try {
                var offsetMs = 0;
                var audioFrames = new List<AudioFrame>();
                var sequence = new ReadOnlySequence<byte>();
                await foreach (var data in byteStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    sequence = sequence.Append(data);

                    if (!headerTask.IsCompleted) {
                        if (sequence.Length < ActualOpusStreamHeader.Prefix.Length + 1)
                            continue;

                        ReadHeader(ref sequence, ref headerSource);
                    }

                    ReadFrames(ref sequence, audioFrames, ref offsetMs);
                    foreach (var audioFrame in audioFrames)
                        await target.Writer.WriteAsync(audioFrame, cancellationToken).ConfigureAwait(false);
                    audioFrames.Clear();

                    static void ReadHeader(ref ReadOnlySequence<byte> sequence, ref TaskCompletionSource<ActualOpusStreamHeader> headerSource)
                    {
                        var header = ActualOpusStreamHeader.Parse(ref sequence);
                        headerSource.SetResult(header);
                    }

                    static void ReadFrames(ref ReadOnlySequence<byte> sequence, List<AudioFrame> frames1, ref int offsetMs)
                    {
                        const int uShortSize = sizeof(ushort);
                        Span<byte> buffer = stackalloc byte[uShortSize];
                        while (true) {
                            if (sequence.Length < uShortSize)
                                return;

                            var sizeSequence = sequence.Slice(0, uShortSize);
                            ushort packetSize;
                            if (sizeSequence.IsSingleSegment)
                                packetSize = BinaryPrimitives.ReadUInt16BigEndian(sizeSequence.FirstSpan);
                            else {
                                sizeSequence.CopyTo(buffer);
                                packetSize = BinaryPrimitives.ReadUInt16BigEndian(buffer);
                            }
                            if (sequence.Length < packetSize + uShortSize)
                                return;

                            sequence = sequence.Slice(uShortSize);
                            var packetSequence = sequence.Slice(0, packetSize);
                            var packet = packetSequence.ToArray();
                            sequence = sequence.Slice(packetSize);
                            offsetMs += 20; // 20-ms frames
                            if (offsetMs >= 0)
                                frames1.Add(new AudioFrame {
                                    Data = packet,
                                    Offset = TimeSpan.FromMilliseconds(offsetMs),
                                    Duration = TimeSpan.FromMilliseconds(20),
                                });
                        }
                    }
                }
            }
            catch (OperationCanceledException e) {
                target.Writer.TryComplete(e);
                if (cancellationToken.IsCancellationRequested)
                    headerSource.TrySetCanceled(cancellationToken);
                else
                    headerSource.TrySetCanceled();
                throw;
            }
            catch (Exception e) {
                Log.LogError(e, "Actual Opus stream Parse failed");
                target.Writer.TryComplete(e);
                headerSource.TrySetException(e);
                throw;
            }
            finally {
                target.Writer.TryComplete();
                if (!headerTask.IsCompleted)
                    headerSource.TrySetException(new InvalidOperationException("Format wasn't parsed."));
            }
        }, CancellationToken.None);

        var (createdAt, format) = await headerTask.ConfigureAwait(false);
        var audioSource = new AudioSource(
            createdAt,
            format,
            target.Reader.ReadAllAsync(cancellationToken),
            TimeSpan.Zero,
            Log,
            cancellationToken);
        return audioSource;
    }

    public async IAsyncEnumerable<(byte[] Buffer, AudioFrame? LastFrame)> ToByteFrameStream(
        AudioSource source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var bufferLease = MemoryPool<byte>.Shared.Rent(4 * 1024);
        var buffer = bufferLease.Memory;
        yield return (WriteHeader(source), null);

        var framesInChunk = 0;
        var position = 0;
        AudioFrame? lastFrame = null;
        await foreach (var frame in source.GetFrames(cancellationToken).ConfigureAwait(false)) {
            lastFrame = frame;
            position += WriteFrame(frame.Data, buffer.Span[position..]);
            framesInChunk++;

            if (framesInChunk >= FramesPerChunk) {
                yield return (buffer.Span[..position].ToArray(), lastFrame);
                framesInChunk = 0;
                position = 0;
            }
        }
        if (position > 0)
            yield return (buffer.Span[..position].ToArray(), lastFrame);

        yield break;

        int WriteFrame(byte[] frame, Span<byte> span)
        {
            ushort length = (ushort)frame.Length;
            BinaryPrimitives.WriteUInt16BigEndian(span, length);
            frame.CopyTo(span[2..]);
            return 2 + frame.Length;
        }

        byte[] WriteHeader(AudioSource audioSource)
            => new ActualOpusStreamHeader(audioSource.CreatedAt, audioSource.Format).Serialize();
    }
}
