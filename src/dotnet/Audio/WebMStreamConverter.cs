using System.Buffers;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;

namespace ActualChat.Audio;

public sealed class WebMStreamConverter : IAudioStreamConverter
{
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public string WritingApp { get; init; } = "actual-chat";
    public ulong? TrackUid { get; init; }
    public int FramesPerChunk { get; init; } = 5;

    public WebMStreamConverter(MomentClockSet clocks, ILogger log)
    {
        Clocks = clocks;
        Log = log;
    }

    public async Task<AudioSource> FromByteStream(
        IAsyncEnumerable<byte[]> byteStream,
        CancellationToken cancellationToken = default)
    {
        var formatSource = TaskCompletionSourceExt.New<AudioFormat>();
        var formatTask = formatSource.Task;

        var clusterOffsetMs = 0;
        EBML? ebml = null;
        Segment? segment = null;
        var formatBlocks = new List<byte[]>();
        var state = new WebMReader.State();
        var frameBuffer = new List<AudioFrame>();
        var readBuffer = ArrayBuffer<byte>.Lease(false, 32 * 1024);
        var blockOffset = TimeSpan.Zero;

        // We're doing this fairly complex processing via tasks & channels only
        // because "async IAsyncEnumerable<..>" methods can't contain
        // "yield return" inside "catch" blocks, and we need this here.
        var target = Channel.CreateBounded<AudioFrame>(
            new BoundedChannelOptions(Constants.Queues.WebMStreamConverterQueueSize) {
                SingleWriter = true,
                SingleReader = true,
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.Wait,
            });

        _ = BackgroundTask.Run(async () => {
            try {
                await foreach (var data in byteStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    AppendData(ref readBuffer, ref state, data);
                    frameBuffer.Clear();
                    state = FillFrameBuffer(
                        WebMReader.FromState(state).WithNewSource(readBuffer.Span),
                        formatSource,
                        formatBlocks,
                        frameBuffer,
                        ref blockOffset,
                        ref ebml,
                        ref segment,
                        ref clusterOffsetMs);

                    foreach (var frame in frameBuffer)
                        await target.Writer.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException e) {
                target.Writer.TryComplete(e);
                if (cancellationToken.IsCancellationRequested)
                    formatSource.TrySetCanceled(cancellationToken);
                else
                    formatSource.TrySetCanceled();
                throw;
            }
            catch (Exception e) {
                Log.LogError(e, "Parse failed");
                target.Writer.TryComplete(e);
                formatSource.TrySetException(e);
                throw;
            }
            finally {
                target.Writer.TryComplete();
                if (!formatTask.IsCompleted)
                    formatSource.TrySetException(new InvalidOperationException("Format wasn't parsed."));
            }
        }, CancellationToken.None);

        var format = await formatTask.ConfigureAwait(false);
        var audioSource = new AudioSource(
            Clocks.SystemClock.Now,
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
        var random = new Random();
        using var bufferLease = MemoryPool<byte>.Shared.Rent(4 * 1024);
        var buffer = bufferLease.Memory;
        var position = 0;
        var header = new EBML {
            EBMLVersion = 1,
            EBMLReadVersion = 1,
            EBMLMaxIDLength = 4,
            EBMLMaxSizeLength = 8,
            DocType = "webm",
            DocTypeVersion = 4,
            DocTypeReadVersion = 2,
        };
        position += WriteModel(header, buffer.Span[position..]);
        var preSkipFrames = source.Format.PreSkipFrames;
        var segment = new Segment {
            Info = new Info {
                TimestampScale = 1000000,
                MuxingApp = WritingApp,
                WritingApp = WritingApp,
            },
            Tracks = new Tracks {
                TrackEntries = new[] {
                    new TrackEntry {
                        TrackNumber = 1,
                        TrackUID = TrackUid ?? (ulong)Math.Abs(random.NextInt64()) & 0x0000FF_FFFF_FFFF_FFFF,
                        TrackType = TrackType.Audio,
                        CodecID = "A_OPUS",
                        CodecPrivate = new byte[] {
                            0x4F, 0x70, 0x75, 0x73, 0x48, 0x65, 0x61, 0x64,
                            0x01, 0x01,
                            (byte)(0xFF & preSkipFrames),
                            (byte)(0xFF & (preSkipFrames >> 8)),
                            0x80, 0xBB, 0x00, 0x00,
                            0x00, 0x00, 0x00,
                        },
                        Audio = new WebM.Models.Audio {
                            SamplingFrequency = 48_000,
                            Channels = 1,
                            BitDepth = 32,
                        },
                        CodecDelay = preSkipFrames == 0
                            ? null
                            :((ulong)preSkipFrames) * 1_000_000_000 / 48_000,
                        SeekPreRoll = preSkipFrames == 0
                            ? 0UL
                            : 80000000UL,
                    },
                },
            },
        };
        position += WriteModel(segment, buffer.Span[position..]);
        var cluster = new Cluster {
            Timestamp = 0,
        };
        position += WriteModel(cluster, buffer.Span[position..]);
        yield return (buffer.Span[..position].ToArray(), null);

        var frames = source.GetFrames(cancellationToken);
        short offsetMs = 0;
        var framesInChunk = 0;
        position = 0;
        AudioFrame? lastFrame = null;
        await foreach (var frame in frames.ConfigureAwait(false)) {
            lastFrame = frame;
            if (offsetMs == 30000) {
                cluster = new Cluster {
                    Timestamp = cluster.Timestamp + (ulong)offsetMs,
                };
                position += WriteModel(cluster, buffer.Span[position..]);
                offsetMs = 0;
            }
            var block = new SimpleBlock {
                TrackNumber = 1,
                TimeCode = offsetMs,
                IsKeyFrame = true,
                Data = frame.Data,
            };
            position += WriteModel(block, buffer.Span[position..]);
            offsetMs += 20;
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

        int WriteModel(BaseModel model, Span<byte> span)
        {
            var writer = new WebMWriter(span);
            if (!writer.Write(model))
                throw new InvalidOperationException("Error writing WebM stream. Buffer is too small.");

            return writer.Position;
        }
    }

    private static void AppendData(ref ArrayBuffer<byte> buffer, ref WebMReader.State state, byte[] data)
    {
        var remainder = buffer.Span.Slice(state.Position, state.Remaining);
        var newLength = remainder.Length + data.Length;
        buffer.EnsureCapacity(newLength);
        buffer.Count = newLength;
        remainder.CopyTo(buffer.Span);
        data.CopyTo(buffer.Span[remainder.Length..]);
    }

    private static WebMReader.State FillFrameBuffer(
        WebMReader webMReader,
        TaskCompletionSource<AudioFormat> formatTaskSource,
        List<byte[]> formatBlocks,
        List<AudioFrame> frames,
        ref TimeSpan blockOffset,
        ref EBML? ebml,
        ref Segment? segment,
        ref int clusterOffsetMs)
    {
        using var bufferLease = MemoryPool<byte>.Shared.Rent(16 * 1024);
        while (webMReader.Read()) {
            var state = webMReader.GetState();
            switch (webMReader.ReadResultKind) {
            case WebMReadResultKind.None:
                // AY: Suspicious - any chance this result means "can't parse anything yet, read further"?
                // AK: no - it means unexpected error  - because returning `true` from Read method indicates that
                // there is something parsed.
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
                if (!formatTaskSource.Task.IsCompleted) {
                    var formatBlocksLength = formatBlocks.Sum(b => b.Length);
                    var beforeFramesStart = webMReader.Span[..state.Position];
                    using var formatBufferLease = MemoryPool<byte>.Shared.Rent(4 * 1024);
                    var formatBuffer = formatBufferLease.Memory.Span[..(formatBlocksLength + beforeFramesStart.Length)];
                    var writtenAt = 0;
                    foreach (var formatBlock in formatBlocks) {
                        formatBlock.CopyTo(formatBuffer[writtenAt..]);
                        writtenAt += formatBlock.Length;
                    }
                    beforeFramesStart.CopyTo(formatBuffer[writtenAt..]);
                    var format = CreateMediaFormat(ebml!, segment!, formatBuffer);
                    formatTaskSource.SetResult(format);
                }
                else
                    clusterOffsetMs = (int)cluster.Timestamp;
                break;
            case WebMReadResultKind.Block:
                var block = (Block)webMReader.ReadResult;
                if (block is SimpleBlock { IsKeyFrame: true } simpleBlock) {
                    var frameOffset = TimeSpan.FromTicks( // To avoid floating-point errors
                        TimeSpan.TicksPerMillisecond * (clusterOffsetMs + block.TimeCode));
                    var duration = frameOffset - blockOffset;
                    if (duration == TimeSpan.Zero)
                        duration = simpleBlock.Data!.Length < 100
                            ? TimeSpan.FromMilliseconds(20)
                            : TimeSpan.FromMilliseconds(60);
                    var mediaFrame = new AudioFrame {
                            Data = simpleBlock.Data!,
                            Offset = frameOffset,
                            Duration = duration,
                        };
                    blockOffset = frameOffset;
                    frames.Add(mediaFrame);
                }
                break;
            case WebMReadResultKind.BlockGroup:
            default:
                throw new NotSupportedException("Unsupported EbmlEntryType.");
            }
        }

        return webMReader.GetState();
    }

    // ReSharper disable once UnusedParameter.Local
    private static AudioFormat CreateMediaFormat(EBML ebml, Segment segment, ReadOnlySpan<byte> rawHeader)
    {
        var trackEntry =
            segment.Tracks?.TrackEntries.Single(t => t.TrackType == TrackType.Audio)
            ?? throw new InvalidOperationException("Stream doesn't contain Audio track.");
        var audio =
            trackEntry.Audio
            ?? throw new InvalidOperationException("Track doesn't contain Audio entry.");

        return new AudioFormat {
            ChannelCount = (short) audio.Channels,
            CodecKind = trackEntry.CodecID switch {
                "A_OPUS" => AudioCodecKind.Opus,
                _ => throw new NotSupportedException($"Unsupported CodecID: {trackEntry.CodecID}."),
            },
            SampleRate = (int) audio.SamplingFrequency,
            CodecSettings = Convert.ToBase64String(rawHeader),
            PreSkipFrames = (int)(trackEntry.CodecDelay ?? 0),
        };
    }
}
