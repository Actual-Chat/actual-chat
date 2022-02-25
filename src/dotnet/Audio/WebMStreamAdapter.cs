using System.Buffers;
using System.IO.Pipelines;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;

namespace ActualChat.Audio;

public class WebMStreamAdapter : IAudioStreamAdapter
{
    private static readonly byte[] WebMHeader = { 0x1A, 0x45, 0xDF, 0xA3 };
    public ILogger<WebMStreamAdapter> Log { get; }

    public WebMStreamAdapter(ILogger<WebMStreamAdapter> log)
        => Log = log;

    public async Task<AudioSource> Read(Stream stream, CancellationToken cancellationToken)
    {
        var reader = PipeReader.Create(stream);
        var headerBlock = await reader.ReadAtLeastAsync(4, cancellationToken).ConfigureAwait(false);
        if (!IsHeaderValid(headerBlock.Buffer))
            throw new InvalidOperationException("Stream doesn't have valid WebM header");

        var formatTask = TaskSource.New<AudioFormat>(true).Task;
        var formatTaskSource = TaskSource.For(formatTask);
        var clusterOffsetMs = 0;
        EBML? ebml = null;
        Segment? segment = null;
        var formatBlocks = new List<byte[]>();
        var state = new WebMReader.State();
        var frameBuffer = new List<AudioFrame>();

        // We're doing this fairly complex processing via tasks & channels only
        // because "async IAsyncEnumerable<..>" methods can't contain
        // "yield return" inside "catch" blocks, and we need this here.

        var target = Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(128) {
            SingleWriter = true,
            SingleReader = true,
            AllowSynchronousContinuations = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        _ = BackgroundTask.Run(async () => {
            try {
                while (true) {
                    var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    var buffer = result.Buffer;
                    frameBuffer.Clear();
                    state = FillFrameBuffer(
                        state,
                        formatTaskSource,
                        formatBlocks,
                        frameBuffer,
                        ref buffer,
                        ref ebml,
                        ref segment,
                        ref clusterOffsetMs);

                    foreach (var frame in frameBuffer)
                        await target.Writer.WriteAsync(frame, cancellationToken).ConfigureAwait(false);

                    reader.AdvanceTo(buffer.Start, buffer.End);
                    if (result.IsCompleted)
                        break;
                }
                await reader.CompleteAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException e) {
                target.Writer.TryComplete(e);
                if (cancellationToken.IsCancellationRequested)
                    formatTaskSource.TrySetCanceled(cancellationToken);
                else
                    formatTaskSource.TrySetCanceled();
                throw;
            }
            catch (Exception e) {
                Log.LogError(e, "Parse failed");
                target.Writer.TryComplete(e);
                formatTaskSource.TrySetException(e);
                throw;
            }
            finally {
                target.Writer.TryComplete();
                if (!formatTask.IsCompleted)
                    formatTaskSource.TrySetException(new InvalidOperationException("Format wasn't parsed."));
            }
        }, CancellationToken.None);

        return new AudioSource(formatTask, target.Reader.ReadAllAsync(cancellationToken), TimeSpan.Zero, Log, cancellationToken);

        bool IsHeaderValid(in ReadOnlySequence<byte> buffer)
        {
            var slice = buffer.Slice(buffer.Start, 4);
            if (slice.IsSingleSegment)
                return slice.FirstSpan.StartsWith(WebMHeader);

            Span<byte> stackBuffer = stackalloc byte[4];
            slice.CopyTo(stackBuffer);
            return stackBuffer.StartsWith(WebMHeader);
        }
    }

    public Task Write(AudioSource source, Stream target, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    private WebMReader.State FillFrameBuffer(
        WebMReader.State prevState,
        TaskSource<AudioFormat> formatTaskSource,
        List<byte[]> formatBlocks,
        List<AudioFrame> frames,
        ref ReadOnlySequence<byte> buffer,
        ref EBML? ebml,
        ref Segment? segment,
        ref int clusterOffsetMs)
    {
        WebMReader webMReader;
        using var bufferLease = MemoryPool<byte>.Shared.Rent(16 * 1024);

        if (buffer.IsSingleSegment) {
            webMReader = WebMReader.FromState(prevState).WithNewSource(buffer.FirstSpan[prevState.Position..]);
            buffer = buffer.Slice(prevState.Position);
        }
        else {
            var span = bufferLease.Memory.Span;
            var length = Math.Min(buffer.Length, span.Length);
            buffer.Slice(prevState.Position, length).CopyTo(span);
            buffer = buffer.Slice(prevState.Position);
            webMReader = WebMReader.FromState(prevState).WithNewSource(span);
        }

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
                    var mediaFrame = new AudioFrame {
                            Offset = frameOffset,
                            Data = simpleBlock.Data!,
                        };
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
