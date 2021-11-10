using System.Buffers;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using ActualChat.Blobs;
using ActualChat.Media;

namespace ActualChat.Audio;

public class AudioSource : MediaSource<AudioFormat, AudioFrame, AudioStreamPart>
{
    public AudioSource(IAsyncEnumerable<BlobPart> blobStream, TimeSpan skipTo, CancellationToken cancellationToken)
        : base(blobStream, skipTo, cancellationToken) { }
    public AudioSource(Task<AudioFormat> formatTask, IAsyncEnumerable<AudioFrame> frames, CancellationToken cancellationToken)
        : base(formatTask, frames, cancellationToken) { }
    public AudioSource(IAsyncEnumerable<IMediaStreamPart> mediaStream, CancellationToken cancellationToken)
        : base(mediaStream, cancellationToken) { }

    public AudioSource SkipTo(TimeSpan skipTo, CancellationToken cancellationToken)
    {
        if (skipTo < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(skipTo));
        if (skipTo == TimeSpan.Zero)
            return this;
        var blobStream = GetBlobStream(cancellationToken);
        var audio = new AudioSource(blobStream, skipTo, cancellationToken);
        return audio;
    }

    // Protected & private methods

    protected override async IAsyncEnumerable<AudioFrame> Parse(
        IAsyncEnumerable<BlobPart> blobStream,
        TimeSpan skipTo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var duration = TimeSpan.Zero;
        var formatTaskSource = TaskSource.For(FormatTask);
        var durationTaskSource = TaskSource.For(DurationTask);

        var blockOffsetMs = 0;
        var clusterOffsetMs = 0;
        var state = new WebMReader.State();
        var frameBuffer = new List<AudioFrame>();
        using var bufferLease = MemoryPool<byte>.Shared.Rent(32 * 1024);
        try {
            await foreach (var blobPart in blobStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                var (_, data) = blobPart;
                var remainingLength = state.Remaining;
                var buffer = bufferLease.Memory;

                buffer.Slice(state.Position, remainingLength)
                    .CopyTo(buffer[..remainingLength]);
                data.CopyTo(buffer[state.Remaining..]);
                var dataLength = state.Remaining + data.Length;

                try {
                    frameBuffer.Clear();
                    state = FillFrameBuffer(
                        frameBuffer,
                        state.IsEmpty
                            ? new WebMReader(bufferLease.Memory.Span[..dataLength])
                            : WebMReader.FromState(state).WithNewSource(bufferLease.Memory.Span[..dataLength]),
                        skipTo,
                        ref clusterOffsetMs,
                        ref blockOffsetMs);
                }
                catch (Exception ex) {
                    formatTaskSource.TrySetException(ex);
                    durationTaskSource.TrySetException(ex);
                    throw;
                }
                foreach (var frame in frameBuffer) {
                    duration = frame.Offset + frame.Duration;
                    yield return frame;
                }
            }
            durationTaskSource.SetResult(duration);
        }
        finally {
            if (cancellationToken.IsCancellationRequested) {
                formatTaskSource.TrySetCanceled(cancellationToken);
                durationTaskSource.TrySetCanceled(cancellationToken);
            }
            else {
                if (!FormatTask.IsCompleted)
                    formatTaskSource.TrySetException(new InvalidOperationException("Format wasn't parsed."));
                if (!DurationTask.IsCompleted)
                    durationTaskSource.TrySetException(new InvalidOperationException("Duration wasn't parsed."));
            }
        }
    }

    private WebMReader.State FillFrameBuffer(
        List<AudioFrame> frameBuffer,
        WebMReader webMReader,
        TimeSpan skipTo,
        ref int clusterOffsetMs,
        ref int blockOffsetMs)
    {
        var prevPosition = 0;
        var framesStart = 0;
        var currentBlockOffsetMs = 0;
        var skipToMs = (int)skipTo.TotalMilliseconds;
        EBML? ebml = null;
        Segment? segment = null;

        using var bufferLease = MemoryPool<byte>.Shared.Rent(32 * 1024);
        while (webMReader.Read()) {
            var state = webMReader.GetState();
            switch (webMReader.ReadResultKind) {
            case WebMReadResultKind.None:
                // AY: Suspicious - any chance this result means "can't parse anything yet, read further"?
                throw new InvalidOperationException();
            case WebMReadResultKind.Ebml:
                ebml = (EBML)webMReader.ReadResult;
                break;
            case WebMReadResultKind.Segment:
                segment = (Segment)webMReader.ReadResult;
                break;
            case WebMReadResultKind.CompleteCluster:
                break;
            case WebMReadResultKind.BeginCluster:
                if (!FormatTask.IsCompleted) {
                    var formatTaskSource = TaskSource.For(FormatTask);
                    var format = CreateMediaFormat(ebml!, segment!, webMReader.Span[..state.Position]);
                    formatTaskSource.SetResult(format);
                    framesStart = state.Position;
                }
                var cluster = (Cluster)webMReader.ReadResult;
                clusterOffsetMs = (int)cluster.Timestamp;
                currentBlockOffsetMs = 0;
                break;
            case WebMReadResultKind.Block:
                var block = (Block)webMReader.ReadResult;
                currentBlockOffsetMs = Math.Max(currentBlockOffsetMs, block.TimeCode);
                if (block is SimpleBlock { IsKeyFrame: true } simpleBlock) {
                    var frameOffset = TimeSpan.FromTicks( // To avoid floating-point errors
                        TimeSpan.TicksPerMillisecond * (clusterOffsetMs + currentBlockOffsetMs));

                    AudioFrame? mediaFrame = null;
                    if (skipToMs <= 0) {
                        // Simple case: nothing to skip
                        mediaFrame = new AudioFrame {
                            Offset = frameOffset,
                            Data = webMReader.Span[framesStart..state.Position].ToArray(),
                        };
                    }
                    else {
                        // Complex case: we may need to skip this frame
                        var outputFrameOffset = frameOffset - skipTo;
                        if (outputFrameOffset >= TimeSpan.Zero) {
                            simpleBlock.TimeCode -= (short)(skipToMs - clusterOffsetMs);
                            var webMWriter = new WebMWriter(bufferLease.Memory.Span);
                            webMWriter.Write(simpleBlock);
                            mediaFrame = new AudioFrame() {
                                Offset = outputFrameOffset,
                                Data = webMWriter.Written.ToArray(),
                            };
                        }
                    }

                    if (mediaFrame != null)
                        frameBuffer.Add(mediaFrame);
                    framesStart = state.Position;
                    blockOffsetMs = currentBlockOffsetMs;
                }
                break;
            case WebMReadResultKind.BlockGroup:
            default:
                throw new NotSupportedException("Unsupported EbmlEntryType.");
            }
            prevPosition = state.Position;
        }

        if (framesStart != prevPosition)
            throw new InvalidOperationException("Unexpected WebM structure.");

        blockOffsetMs = currentBlockOffsetMs;
        return webMReader.GetState();
    }

    private AudioFormat CreateMediaFormat(EBML ebml, Segment segment, ReadOnlySpan<byte> rawHeader)
    {
        var trackEntry = segment.Tracks?.TrackEntries.Single(t => t.TrackType == TrackType.Audio)
                         ?? throw new InvalidOperationException("Stream doesn't contain Audio track.");
        var audio = trackEntry.Audio
                    ?? throw new InvalidOperationException("Track doesn't contain Audio entry.");

        return new AudioFormat {
            ChannelCount = (int) audio.Channels,
            CodecKind = trackEntry.CodecID switch {
                "A_OPUS" => AudioCodecKind.Opus,
                _ => throw new NotSupportedException($"Unsupported CodecID: {trackEntry.CodecID}."),
            },
            SampleRate = (int) audio.SamplingFrequency,
            CodecSettings = Convert.ToBase64String(rawHeader),
        };
    }
}
