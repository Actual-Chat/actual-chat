using System.Buffers;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using ActualChat.Media;
using ActualChat.Spans;

namespace ActualChat.Audio;

// TODO(AK): simplify this class and extract Parse \ Serialize
// TODO(AK): get rid of WebM container support???
public class AudioSource : MediaSource<AudioFormat, AudioFrame>
{
    private static readonly byte[] ActualOpusStreamHeader = { 0x41, 0x5F, 0x4F, 0x50, 0x55, 0x53, 0x5F, 0x53 }; // A_OPUS_S
    private static readonly byte[] ActualOpusStreamFormat = { 0x41, 0x5F, 0x4F, 0x50, 0x55, 0x53, 0x5F, 0x53, 0x01 }; // A_OPUS_S + version = 1
    private static readonly byte[] WebMHeader = { 0x1A, 0x45, 0xDF, 0xA3 };
    protected bool DebugMode => Constants.DebugMode.AudioSource;
    protected ILogger? DebugLog => DebugMode ? Log : null;
    private bool ShouldStripWebM { get; init; }

    public static AudioFormat DefaultFormat => new() {
        CodecSettings = Convert.ToBase64String(ActualOpusStreamFormat),
    };

    public AudioSource(
        IAsyncEnumerable<byte[]> byteStream,
        TimeSpan skipTo,
        ILogger log,
        CancellationToken cancellationToken) : base(byteStream, skipTo, log, cancellationToken)
    { }

    public AudioSource(
        Task<AudioFormat> formatTask,
        IAsyncEnumerable<AudioFrame> frameStream,
        TimeSpan skipTo,
        ILogger log,
        CancellationToken cancellationToken)
        : base(formatTask,
            frameStream
                .SkipWhile(af => af.Offset < skipTo)
                .Select(af => new AudioFrame {
                    Data = af.Data,
                    Offset = af.Offset - skipTo,
                }),
            log,
            cancellationToken)
    { }

    public AudioSource StripWebM(CancellationToken cancellationToken)
    {
        var byteStream = GetFrames(cancellationToken).ToByteStream(FormatTask, cancellationToken);
        var audio = new AudioSource(byteStream, TimeSpan.Zero, Log, cancellationToken) {
            ShouldStripWebM = true,
        };
        return audio;
    }

    public AudioSource SkipTo(TimeSpan skipTo, CancellationToken cancellationToken)
    {
        if (skipTo < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(skipTo));
        if (skipTo == TimeSpan.Zero)
            return this;

        if (ShouldStripWebM)
            throw new InvalidOperationException("SkipTo is not supported when WebM container has been stripped");

        return new AudioSource(FormatTask, GetFrames(cancellationToken), skipTo, Log, cancellationToken);
    }

    // Protected & private methods
    protected override async IAsyncEnumerable<AudioFrame> Parse(
        IAsyncEnumerable<byte[]> byteStream,
        TimeSpan skipTo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var firstBlockLength = 0;
        var blockList = new List<byte[]>();
        var byteBlockEnumerator = byteStream.GetAsyncEnumerator(cancellationToken);
        while (firstBlockLength <= 128) {
            var hasNext = await byteBlockEnumerator.MoveNextAsync().ConfigureAwait(false);
            if (!hasNext) {
                var formatTaskSource = TaskSource.For(FormatTask);
                formatTaskSource.TrySetException(new InvalidOperationException("Format wasn't parsed."));
                var durationTaskSource = TaskSource.For(DurationTask);
                durationTaskSource.TrySetResult(TimeSpan.Zero);
                yield break;
            }

            var byteBlock = byteBlockEnumerator.Current;
            firstBlockLength += byteBlock.Length;
            blockList.Add(byteBlock);
        }

        var firstBlock = blockList.Count == 1
            ? blockList[0]
            : blockList.Aggregate(
                (resultBlock: new byte[firstBlockLength], offset: 0),
                (result, block) => {
                    Buffer.BlockCopy(block,
                        0,
                        result.resultBlock,
                        result.offset,
                        block.Length);
                    return (result.resultBlock, result.offset + block.Length);
                }).resultBlock;
        var restoredByteStream = byteBlockEnumerator.Prepend(firstBlock, cancellationToken);

        ChannelReader<AudioFrame> reader;
        if (firstBlock.StartsWith(ActualOpusStreamHeader))
            reader = ParseActualOpusStream(restoredByteStream, skipTo, cancellationToken);
        else if (firstBlock.StartsWith(WebMHeader))
            reader = ParseWebMStream(restoredByteStream, skipTo, cancellationToken);
        else
            throw new NotSupportedException(
                $"Invalid audio stream: ${string.Join('-', firstBlock.Take(10).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)))}");

        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        while (reader.TryRead(out var frame))
            yield return frame;
    }

    private ChannelReader<AudioFrame> ParseActualOpusStream(
        IAsyncEnumerable<byte[]> byteStream,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var formatTaskSource = TaskSource.For(FormatTask);
        var durationTaskSource = TaskSource.For(DurationTask);
        var readBuffer = ArrayBuffer<byte>.Lease(false, 2 * 1024);

        // We're doing this fairly complex processing via tasks & channels only
        // because "async IAsyncEnumerable<..>" methods can't contain
        // "yield return" inside "catch" blocks, and we need this here.
        var target = Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(128) {
            SingleWriter = true,
            SingleReader = true,
            AllowSynchronousContinuations = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        var _ = BackgroundTask.Run(async () => {
            try {
                var buffered = 0;
                var position = 0;
                var offsetMs = -(int)skipTo.TotalMilliseconds;
                var audioFrames = new List<AudioFrame>();
                await foreach (var data in byteStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    Buffer(ref readBuffer, data, ref position, ref buffered);

                    if (!FormatTask.IsCompleted) {
                        if (buffered < ActualOpusStreamHeader.Length + 1)
                            continue;

                        if (!readBuffer.Buffer.StartsWith(ActualOpusStreamHeader))
                            throw new InvalidOperationException("Actual Opus stream header is invalid.");

                        var version = readBuffer.Buffer[ActualOpusStreamHeader.Length];
                        if (version != 1)
                            throw new NotSupportedException($"Actual Opus stream version is invalid - ${version}. Only version 1 is supported.");

                        formatTaskSource.SetResult(DefaultFormat);
                        position = ActualOpusStreamFormat.Length + 1;
                    }


                    position += ReadFrames(readBuffer.Span[position..buffered], audioFrames, ref offsetMs);
                    foreach (var audioFrame in audioFrames)
                        await target.Writer.WriteAsync(audioFrame, cancellationToken).ConfigureAwait(false);
                    audioFrames.Clear();

                    void Buffer(ref ArrayBuffer<byte> buffer, byte[] data1, ref int position1, ref int buffered1)
                    {
                        var bufferedLength = buffered1 - position1;
                        var newLength = bufferedLength + data.Length;
                        buffer.EnsureCapacity(newLength);
                        buffer.Count = newLength;
                        if (position1 > 0) {
                            var remainder = buffer.Span[position1..buffered1];
                            remainder.CopyTo(buffer.Span);
                        }
                        data1.CopyTo(buffer.Span[bufferedLength..]);
                        position1 = 0;
                        buffered1 = newLength;
                    }

                    int ReadFrames(ReadOnlySpan<byte> buffer, List<AudioFrame> frames1, ref int offsetMs1)
                    {
                        var reader = new SpanReader(buffer);
                        var packetSize = reader.ReadVInt(4);
                        while (packetSize.HasValue && reader.Position + (int)packetSize.Value.Value < reader.Length) {
                            var packet = reader.ReadBytes((int)packetSize.Value.Value);
                            if (packet == null)
                                return reader.Position;

                            offsetMs1 += 20; // 20-ms frames
                            if (offsetMs1 >= 0)
                                frames1.Add(new AudioFrame {
                                    Data = packet!,
                                    Offset = TimeSpan.FromMilliseconds(offsetMs1),
                                });
                            packetSize = reader.ReadVInt();
                        }
                        if (!packetSize.HasValue && reader.Position + 4 < reader.Length)
                            throw new InvalidOperationException("Unable to read Opus packet length.");

                        return reader.Position;
                    }
                }

                var durationMs = Math.Max(0, offsetMs + 20);
                durationTaskSource.SetResult(TimeSpan.FromMilliseconds(durationMs));
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

        return target.Reader;


    }

    private ChannelReader<AudioFrame> ParseWebMStream(
        IAsyncEnumerable<byte[]> byteStream,
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
        EBML? ebml = null;
        Segment? segment = null;
        var formatBlocks = new List<byte[]>();
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

        var _ = BackgroundTask.Run(async () => {
            try {
                await foreach (var data in byteStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    AppendData(ref readBuffer, ref state, data);
                    frameBuffer.Clear();
                    state = FillFrameBuffer(
                        frameBuffer,
                        state.IsEmpty
                            ? new WebMReader(readBuffer.Span)
                            : WebMReader.FromState(state).WithNewSource(readBuffer.Span),
                        formatBlocks,
                        ref ebml,
                        ref segment,
                        ref actualSkipTo,
                        ref clusterOffsetMs,
                        ref blockOffsetMs,
                        ref skipAdjustmentClusterMs,
                        ref skipAdjustmentBlockMs);

                    foreach (var frame in frameBuffer) {
                        duration = frame.Offset + frame.Duration;
                        await target.Writer.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                    }
                }
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

        return target.Reader;
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
        List<byte[]> formatBlocks,
        ref EBML? ebml,
        ref Segment? segment,
        ref TimeSpan skipTo,
        ref int clusterOffsetMs,
        ref short blockOffsetMs,
        ref int skipAdjustmentClusterMs,
        ref short skipAdjustmentBlockMs)
    {
        var prevPosition = 0;
        var framesStart = 0;
        var skipToMs = (int)skipTo.TotalMilliseconds;

        using var writeBufferLease = MemoryPool<byte>.Shared.Rent(32 * 1024);
        using var formatBufferLease = MemoryPool<byte>.Shared.Rent(4 * 1024);
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
                    var formatBlocksLength = formatBlocks.Sum(b => b.Length);
                    var beforeFramesStart = webMReader.Span[..state.Position];
                    var formatBuffer = formatBufferLease.Memory.Span[..(formatBlocksLength + beforeFramesStart.Length)];
                    var writtenAt = 0;
                    foreach (var formatBlock in formatBlocks) {
                        formatBlock.CopyTo(formatBuffer[writtenAt..]);
                        writtenAt += formatBlock.Length;
                    }
                    beforeFramesStart.CopyTo(formatBuffer[writtenAt..]);

                    var header = ShouldStripWebM
                        ? segment?.Tracks?.TrackEntries[0].CodecPrivate ?? formatBuffer
                        : formatBuffer;
                    var format = CreateMediaFormat(ebml!, segment!, header);
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
                        // simpleBlock.Data
                        mediaFrame = new AudioFrame {
                            Offset = frameOffset,
                            Data = ShouldStripWebM
                                ? simpleBlock.Data!
                                : webMReader.Span[framesStart..state.Position].ToArray(),

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
                            var outputFrameOffset = frameOffset -
                                TimeSpan.FromMilliseconds(skipAdjustmentClusterMs + skipAdjustmentBlockMs);
                            DebugLog?.LogDebug(
                                "Rewriting audio frame offset: {FrameOffset}s -> {OutputFrameOffset}s",
                                frameOffset.TotalSeconds, outputFrameOffset.TotalSeconds);

                            webMWriter.Write(simpleBlock);
                            mediaFrame = new AudioFrame {
                                Offset = outputFrameOffset,
                                Data = ShouldStripWebM
                                    ? simpleBlock.Data!
                                    : webMWriter.Written.ToArray(),
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

        if (!FormatTask.IsCompleted)
            formatBlocks.Add(webMReader.Span.ToArray());

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
