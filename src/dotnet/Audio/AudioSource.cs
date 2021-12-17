using System.Buffers;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using ActualChat.Media;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActualChat.Audio;

public class AudioSource : MediaSource<AudioFormat, AudioFrame, AudioStreamPart>
{
    protected bool DebugMode => Constants.DebugMode.AudioSource;
    protected ILogger? DebugLog => DebugMode ? Log : null;

    protected override AudioFormat DefaultFormat => new AudioFormat {
        CodecSettings = "GkXfo59ChoEBQveBAULygQRC84EIQoKEd2VibUKHgQJChYECGFOAZwH/////////FUmpZrMq17GD"
            + "D0JATYCTb3B1cy1tZWRpYS1yZWNvcmRlcldBk29wdXMtbWVkaWEtcmVjb3JkZXIWVK5rv66914EB"
            + "c8WHtvVVEG3dyIOBAoaGQV9PUFVTY6KTT3B1c0hlYWQBAQAAgLsAAAAAAOGNtYRHO4AAn4EBYmSB"
            + "IB9DtnUB/////////+eBAA==",
    };

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
        var actualSkipTo = skipTo;
        var clusterOffsetMs = 0;
        short blockOffsetMs = 0;
        var skipAdjustmentBlockMs = short.MinValue;
        var skipAdjustmentClusterMs = int.MinValue;
        var state = new WebMReader.State();
        var frameBuffer = new List<AudioFrame>();
        var readBuffer = ArrayBuffer<byte>.Lease(false, 32 * 1024);

        // We're doing this fairly complex processing via tasks & channels only
        // because "async IAsyncEnumerable<..>" methods can't contain
        // "yield return" inside "catch" blocks, and we need this here.

        var target = Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(128) {
            SingleWriter = true,
            SingleReader = true,
            AllowSynchronousContinuations = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        var parseTask = BackgroundTask.Run(() => blobStream.ForEachAwaitAsync(
            async blobPart => {
                var (_, data) = blobPart;

                AppendData(ref readBuffer, ref state, data);
                frameBuffer.Clear();
                state = FillFrameBuffer(
                    frameBuffer,
                    state.IsEmpty
                        ? new WebMReader(readBuffer.Span)
                        : WebMReader.FromState(state).WithNewSource(readBuffer.Span),
                    ref actualSkipTo,
                    ref clusterOffsetMs,
                    ref blockOffsetMs,
                    ref skipAdjustmentClusterMs,
                    ref skipAdjustmentBlockMs);

                foreach (var frame in frameBuffer) {
                    duration = frame.Offset + frame.Duration;
                    await target.Writer.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
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
                Log.LogError(e, "Parse failed");
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
                readBuffer.Release();
            }
        }, CancellationToken.None);

        return target.Reader.ReadAllAsync(cancellationToken);
    }

    private void AppendData(ref ArrayBuffer<byte> buffer, ref WebMReader.State state, byte[] data)
    {
        var remainder = buffer.Span.Slice(state.Position, state.Remaining);
        var newLength = remainder.Length + data.Length;
        buffer.EnsureCapacity(newLength);
        buffer.Count = newLength;
        remainder.CopyTo(buffer.Span);
        data.CopyTo(buffer.Span[remainder.Length..]);
    }

    private WebMReader.State FillFrameBuffer(
        List<AudioFrame> frameBuffer,
        WebMReader webMReader,
        ref TimeSpan skipTo,
        ref int clusterOffsetMs,
        ref short blockOffsetMs,
        ref int skipAdjustmentClusterMs,
        ref short skipAdjustmentBlockMs)
    {
        var prevPosition = 0;
        var framesStart = 0;
        var skipToMs = (int)skipTo.TotalMilliseconds;
        EBML? ebml = null;
        Segment? segment = null;

        using var writeBufferLease = MemoryPool<byte>.Shared.Rent(32 * 1024);
        var writeBuffer = writeBufferLease.Memory.Span;
        var webMWriter = new WebMWriter(writeBuffer);

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
                var cluster = (Cluster)webMReader.ReadResult;
                if (!FormatTask.IsCompleted) {
                    var formatTaskSource = TaskSource.For(FormatTask);
                    var format = CreateMediaFormat(ebml!, segment!, webMReader.Span[..state.Position]);
                    formatTaskSource.SetResult(format);
                    framesStart = state.Position;
                }
                else {
                    if (cluster.Timestamp - (ulong)clusterOffsetMs > 32768) { // max block offset within the cluster, probably we have a gap
                        clusterOffsetMs += blockOffsetMs;
                        clusterOffsetMs += 20; // hardcoded!!! it makes sense to calculate average block duration
                        cluster.Timestamp = (ulong)clusterOffsetMs;
                    }
                    else
                        clusterOffsetMs = (int)cluster.Timestamp;

                    if (skipAdjustmentBlockMs > 0) {
                        cluster.Timestamp -= (ulong)skipAdjustmentBlockMs;
                        webMWriter.Write(cluster);
                    }
                }
                blockOffsetMs = 0;
                break;
            case WebMReadResultKind.Block:
                var block = (Block)webMReader.ReadResult;
                var prevBlockOffsetMs = blockOffsetMs;

                if (blockOffsetMs == 0 && clusterOffsetMs == 0) {
                    if (block.TimeCode > 60 && skipTo == TimeSpan.Zero) { // audio segment with an offset, 60 is the largest opus frame duration
                        skipTo = TimeSpan.FromMilliseconds(1);
                        skipToMs = 1;
                    }
                }
                blockOffsetMs = block.TimeCode;
                if (blockOffsetMs - prevBlockOffsetMs > 65) {
                    if (blockOffsetMs != 0) {
                        skipAdjustmentBlockMs = (short)(blockOffsetMs - prevBlockOffsetMs - 20);
                        skipAdjustmentClusterMs = clusterOffsetMs;
                        if (skipTo == TimeSpan.Zero) {
                            skipTo = TimeSpan.FromMilliseconds(1);
                            skipToMs = 1;
                        }
                    }
                }

                if (block is SimpleBlock { IsKeyFrame: true } simpleBlock) {
                    var frameOffset = TimeSpan.FromTicks( // To avoid floating-point errors
                        TimeSpan.TicksPerMillisecond * (clusterOffsetMs + blockOffsetMs));

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
                        if (frameOffset - skipTo >= TimeSpan.Zero) {
                            if (skipAdjustmentBlockMs == short.MinValue) {
                                skipAdjustmentBlockMs = blockOffsetMs;
                                skipAdjustmentClusterMs = clusterOffsetMs;
                                simpleBlock.TimeCode = 0;
                            }
                            else if (skipAdjustmentClusterMs >= clusterOffsetMs)
                                simpleBlock.TimeCode -= skipAdjustmentBlockMs;
                            var outputFrameOffset = frameOffset.Subtract(
                                TimeSpan.FromMilliseconds(skipAdjustmentClusterMs + skipAdjustmentBlockMs));
                            DebugLog?.LogDebug(
                                "Rewriting audio frame offset: {FrameOffset}s -> {OutputFrameOffset}s",
                                frameOffset.TotalSeconds, outputFrameOffset.TotalSeconds);

                            webMWriter.Write(simpleBlock);
                            mediaFrame = new AudioFrame {
                                Offset = outputFrameOffset,
                                Data = webMWriter.Written.ToArray(),
                            };
                            webMWriter = new WebMWriter(writeBuffer);
                        }
                    }

                    if (mediaFrame != null)
                        frameBuffer.Add(mediaFrame);
                    framesStart = state.Position;
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

        if (!FormatTask.IsCompleted) {
            var formatTaskSource = TaskSource.For(FormatTask);
            var format = CreateMediaFormat(ebml!, segment!, webMReader.Span);
            formatTaskSource.SetResult(format);
        }

        return webMReader.GetState();
    }

    // ReSharper disable once UnusedParameter.Local
    private AudioFormat CreateMediaFormat(EBML ebml, Segment segment, ReadOnlySpan<byte> rawHeader)
    {
        var trackEntry =
            segment.Tracks?.TrackEntries.Single(t => t.TrackType == TrackType.Audio)
            ?? throw new InvalidOperationException("Stream doesn't contain Audio track.");
        var audio =
            trackEntry.Audio
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
