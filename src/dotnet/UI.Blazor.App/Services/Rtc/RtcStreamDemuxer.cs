using ActualChat.Audio;
using ActualChat.Rtc;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor.App.Services.Rtc;

/// <summary>
/// Demultiplexes a single RTC stream into individual audio streams.
/// Raises events when streams start and end.
/// </summary>
public sealed class RtcStreamDemuxer : IAsyncDisposable
{
    private readonly ConcurrentDictionary<int, Channel<byte[]>> _streamChannels = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _runTask;

    private ILogger Log { get; }

    public event Action<RtcStreamStartedArgs>? StreamStarted;
    public event Action<int>? StreamEnded;

    public RtcStreamDemuxer(ILogger log)
        => Log = log;

    public async Task RunAsync(RpcStream<RtcItem> input, CancellationToken cancellationToken)
    {
        if (_runTask != null)
            throw new InvalidOperationException("Already running.");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        var ct = linkedCts.Token;

        _runTask = ProcessAsync(input, ct);
        await _runTask.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_runTask != null)
            await _runTask.SilentAwait(false);

        foreach (var channel in _streamChannels.Values)
            channel.Writer.TryComplete();

        _streamChannels.Clear();
        _cts.Dispose();
    }

    // Private methods

    private async Task ProcessAsync(RpcStream<RtcItem> input, CancellationToken cancellationToken)
    {
        try {
            await foreach (var item in ((IAsyncEnumerable<RtcItem>)input).WithCancellation(cancellationToken).ConfigureAwait(false)) {
                switch (item) {
                case RtcStreamStart start:
                    HandleStreamStart(start);
                    break;
                case RtcAudioFrame frame:
                    HandleAudioFrame(frame);
                    break;
                case RtcStreamEnd end:
                    HandleStreamEnd(end);
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Expected
        }
        catch (RpcReconnectFailedException) {
            // Connection lost
        }
        catch (Exception e) {
            Log.LogError(e, "Error processing RTC stream");
        }
        finally {
            // Clean up all remaining streams
            foreach (var (streamIndex, channel) in _streamChannels) {
                channel.Writer.TryComplete();
                StreamEnded?.Invoke(streamIndex);
            }
            _streamChannels.Clear();
        }
    }

    private void HandleStreamStart(RtcStreamStart start)
    {
        var channel = Channel.CreateUnbounded<byte[]>(ChannelExt.SingleReaderWriterUnboundedChannelOptions);
        if (!_streamChannels.TryAdd(start.StreamIndex, channel)) {
            Log.LogWarning("Duplicate stream start for index {StreamIndex}", start.StreamIndex);
            return;
        }

        var audioFrames = ToAsyncEnumerable(channel.Reader, _cts.Token);
        var args = new RtcStreamStartedArgs(start, audioFrames);
        StreamStarted?.Invoke(args);
    }

    private void HandleAudioFrame(RtcAudioFrame frame)
    {
        if (!_streamChannels.TryGetValue(frame.StreamIndex, out var channel)) {
            // Stream not found - might have ended already
            return;
        }

        if (!channel.Writer.TryWrite(frame.Data))
            Log.LogWarning("Failed to write frame for stream {StreamIndex}", frame.StreamIndex);
    }

    private void HandleStreamEnd(RtcStreamEnd end)
    {
        if (!_streamChannels.TryRemove(end.StreamIndex, out var channel))
            return;

        channel.Writer.TryComplete();
        StreamEnded?.Invoke(end.StreamIndex);
    }

    private static async IAsyncEnumerable<byte[]> ToAsyncEnumerable(
        ChannelReader<byte[]> reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }
}

/// <summary>
/// Arguments for the StreamStarted event.
/// </summary>
public sealed record RtcStreamStartedArgs(
    RtcStreamStart StreamInfo,
    IAsyncEnumerable<byte[]> AudioFrames)
{
    public int StreamIndex => StreamInfo.StreamIndex;
    public Moment BeginsAt => StreamInfo.BeginsAt;
    public AuthorId? AuthorId => StreamInfo.AuthorId;
    public ChatEntryId? EntryId => StreamInfo.EntryId;
    public AudioFormat? Format => StreamInfo.Format;
}
