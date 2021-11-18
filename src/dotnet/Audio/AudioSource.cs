using System.Buffers;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using ActualChat.Blobs;
using ActualChat.Media;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActualChat.Audio;

public class AudioSource : MediaSource<AudioFormat, AudioFrame, AudioStreamPart>
{
    private static readonly byte[] BrokenHeader = { 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86 };

    public AudioSource(IAsyncEnumerable<BlobPart> blobStream, TimeSpan skipTo, ILogger? log, CancellationToken cancellationToken)
        : base(blobStream, skipTo, log ?? NullLogger.Instance, cancellationToken) { }
    public AudioSource(IAsyncEnumerable<IMediaStreamPart> mediaStream, ILogger? log, CancellationToken cancellationToken)
        : base(mediaStream, log ?? NullLogger.Instance, cancellationToken) { }

    public AudioSource SkipTo(TimeSpan skipTo, CancellationToken cancellationToken)
    {
        if (skipTo < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(skipTo));
        if (skipTo == TimeSpan.Zero)
            return this;
        var blobStream = GetBlobStream(cancellationToken);
        var audio = new AudioSource(blobStream, skipTo, Log, cancellationToken);
        return audio;
    }

    // Protected & private methods

    protected override IAsyncEnumerable<AudioFrame> Parse(
        IAsyncEnumerable<BlobPart> blobStream,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var duration = TimeSpan.Zero;
        var formatTaskSource = TaskSource.For(FormatTask);
        var durationTaskSource = TaskSource.For(DurationTask);

        var blockOffsetMs = 0;
        var clusterOffsetMs = 0;
        var state = new WebMReader.State();
        var frameBuffer = new List<AudioFrame>();
        var readBufferLease = MemoryPool<byte>.Shared.Rent(32 * 1024); // Disposed in the last "finally"
        var readBuffer = readBufferLease.Memory;

        // We're doing this fairly complex processing via tasks & channels only
        // because "async IAsyncEnumerable<..>" methods can't contain
        // "yield return" inside "catch" blocks, and we need this here.

        var target = Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(128) {
            SingleWriter = true,
            SingleReader = true,
            AllowSynchronousContinuations = true,
        });

        var parseTask = BackgroundTask.Run(() => blobStream.ForEachAwaitAsync(
            async blobPart => {
                var (_, data) = blobPart;
                var remainingLength = state.Remaining;

                // AK: broken stream check
                if (data.Take(6).SequenceEqual(BrokenHeader)) {
                    Log.LogWarning("Recorded broken header");
                    var fixedChunk = new byte[data.Length + 1];
                    fixedChunk[0] = 0x1A;
                    Buffer.BlockCopy(data, 0, fixedChunk, 1, data.Length);
                    data = fixedChunk;
                }

                readBuffer.Span.Slice(state.Position, remainingLength)
                    .CopyTo(readBuffer.Span[..remainingLength]);
                data.CopyTo(readBuffer[state.Remaining..]);
                var dataLength = state.Remaining + data.Length;

                frameBuffer.Clear();
                state = FillFrameBuffer(
                    frameBuffer,
                    state.IsEmpty
                        ? new WebMReader(readBuffer.Span[..dataLength])
                        : WebMReader.FromState(state).WithNewSource(readBufferLease.Memory.Span[..dataLength]),
                    skipTo,
                    ref clusterOffsetMs,
                    ref blockOffsetMs);

                foreach (var frame in frameBuffer) {
                    duration = frame.Offset + frame.Duration;
                    await target.Writer.WriteAsync(frame, cancellationToken);
                }
            }, cancellationToken), cancellationToken);

        var _ = BackgroundTask.Run(async () => {
            try {
                await parseTask.ConfigureAwait(false);
                durationTaskSource.SetResult(duration);
            }
            catch (OperationCanceledException e) {
                target.Writer.TryComplete(e);
                if (cancellationToken.IsCancellationRequested) {
                    formatTaskSource.TrySetCanceled(cancellationToken);
                    durationTaskSource.TrySetCanceled(cancellationToken);
                }
                else {
                    formatTaskSource.TrySetCanceled();
                    durationTaskSource.TrySetCanceled();
                }
                throw;
            }
            catch (Exception e) {
                Log.LogError(e, "BlobPart Parse failed");
                target.Writer.TryComplete(e);
                formatTaskSource.TrySetException(e);
                durationTaskSource.TrySetException(e);
                throw;
            }
            finally {
                target.Writer.TryComplete();
                if (!FormatTask.IsCompleted)
                    formatTaskSource.TrySetException(new InvalidOperationException("Format wasn't parsed."));
                if (!DurationTask.IsCompleted)
                    durationTaskSource.TrySetException(new InvalidOperationException("Duration wasn't parsed."));
                readBufferLease.Dispose();
            }
        }, CancellationToken.None);

        return target.Reader.ReadAllAsync(cancellationToken);
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

        using var writeBufferLease = MemoryPool<byte>.Shared.Rent(32 * 1024);
        var writeBuffer = writeBufferLease.Memory;

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
                            var webMWriter = new WebMWriter(writeBuffer.Span);
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
