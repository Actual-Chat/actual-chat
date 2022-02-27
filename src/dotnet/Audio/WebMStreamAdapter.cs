using System.Buffers;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;

namespace ActualChat.Audio;

public class WebMStreamAdapter : IAudioStreamAdapter
{
    public ILogger Log { get; }

    public WebMStreamAdapter(ILogger log)
        => Log = log;

    public Task<AudioSource> Read(IAsyncEnumerable<byte[]> byteStream, CancellationToken cancellationToken)
    {
        var formatTask = TaskSource.New<AudioFormat>(true).Task;
        var formatTaskSource = TaskSource.For(formatTask);
        var clusterOffsetMs = 0;
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

        _ = BackgroundTask.Run(async () => {
            try {
                await foreach (var data in byteStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    AppendData(ref readBuffer, ref state, data);
                    frameBuffer.Clear();
                    state = FillFrameBuffer(
                        WebMReader.FromState(state).WithNewSource(readBuffer.Span),
                        formatTaskSource,
                        formatBlocks,
                        frameBuffer,
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

        var audioSource = new AudioSource(formatTask, target.Reader.ReadAllAsync(cancellationToken), TimeSpan.Zero, Log, cancellationToken);
        return Task.FromResult(audioSource);
    }

    public IAsyncEnumerable<byte[]> Write(AudioSource source, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
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
        WebMReader webMReader,
        TaskSource<AudioFormat> formatTaskSource,
        List<byte[]> formatBlocks,
        List<AudioFrame> frames,
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
