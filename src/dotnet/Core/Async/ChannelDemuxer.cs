namespace ActualChat.Async;

/// <summary>
/// Splits a single channel of IMuxable items into multiple channels by StreamIndex.
/// </summary>
public sealed class ChannelDemuxer<T> : IAsyncDisposable
    where T : IMuxable
{
    private readonly ConcurrentDictionary<int, Channel<T>> _channels = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly int? _channelCapacity;
    private Task? _pumpTask;
    private volatile bool _isDisposed;

    public ChannelDemuxer(int? channelCapacity = null)
        => _channelCapacity = channelCapacity;

    public void SetInput(ChannelReader<T> input)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_pumpTask != null)
            throw new InvalidOperationException("Input is already set.");

        _pumpTask = PumpAsync(input);
    }

    public ChannelReader<T> GetOrCreateChannel(int streamIndex)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var channel = _channels.GetOrAdd(streamIndex, _ => CreateChannel());
        return channel.Reader;
    }

    public bool TryRemoveChannel(int streamIndex)
    {
        if (!_channels.TryRemove(streamIndex, out var channel))
            return false;

        channel.Writer.TryComplete();
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        await _cts.CancelAsync().ConfigureAwait(false);

        if (_pumpTask != null)
            await _pumpTask.SilentAwait(false);

        foreach (var channel in _channels.Values)
            channel.Writer.TryComplete();

        _channels.Clear();
        _cts.Dispose();
    }

    // Private methods

    private Channel<T> CreateChannel()
        => ChannelExt.Create<T>(_channelCapacity, singleReader: true, singleWriter: true);

    private async Task PumpAsync(ChannelReader<T> input)
    {
        var ct = _cts.Token;
        Exception? error = null;
        try {
            while (await input.WaitToReadAsync(ct).ConfigureAwait(false))
                while (input.TryRead(out var item)) {
                    if (_channels.TryGetValue(item.StreamIndex, out var channel))
                        await channel.Writer.WriteAsync(item, ct).ConfigureAwait(false);
                    // Items for unknown stream indices are silently dropped
                }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            // Expected during disposal
        }
        catch (Exception e) {
            error = e;
            throw;
        }
        finally {
            foreach (var channel in _channels.Values)
                channel.Writer.TryComplete(error);
        }
    }
}
