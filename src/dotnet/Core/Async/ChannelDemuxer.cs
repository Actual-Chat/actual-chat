namespace ActualChat.Async;

/// <summary>
/// Splits a single channel into multiple channels by key.
/// </summary>
public sealed class ChannelDemuxer<TKey, TItem> : IAsyncDisposable
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Channel<TItem>> _channels = new();
    private readonly Func<TItem, TKey> _keySelector;
    private readonly Func<TItem, bool>? _isCloseSignal;
    private readonly CancellationTokenSource _cts = new();
    private readonly int? _channelCapacity;
    private Task? _pumpTask;
    private volatile bool _isDisposed;

    public ChannelDemuxer(
        Func<TItem, TKey> keySelector,
        Func<TItem, bool>? isCloseSignal = null,
        int? channelCapacity = null)
    {
        _keySelector = keySelector;
        _isCloseSignal = isCloseSignal;
        _channelCapacity = channelCapacity;
    }

    public void SetInput(ChannelReader<TItem> input)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_pumpTask != null)
            throw new InvalidOperationException("Input is already set.");

        _pumpTask = PumpAsync(input);
    }

    public ChannelReader<TItem> GetOrCreateChannel(TKey key)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var channel = _channels.GetOrAdd(key, _ => CreateChannel());
        return channel.Reader;
    }

    public bool TryRemoveChannel(TKey key)
    {
        if (!_channels.TryRemove(key, out var channel))
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

    private Channel<TItem> CreateChannel()
        => _channelCapacity.HasValue
            ? Channel.CreateBounded<TItem>(new BoundedChannelOptions(_channelCapacity.Value) {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            })
            : Channel.CreateUnbounded<TItem>(new UnboundedChannelOptions {
                SingleReader = true,
                SingleWriter = true,
            });

    private async Task PumpAsync(ChannelReader<TItem> input)
    {
        var ct = _cts.Token;
        Exception? error = null;
        try {
            while (await input.WaitToReadAsync(ct).ConfigureAwait(false))
                while (input.TryRead(out var item)) {
                    var key = _keySelector(item);

                    if (_isCloseSignal?.Invoke(item) == true) {
                        TryRemoveChannel(key);
                        continue;
                    }

                    if (_channels.TryGetValue(key, out var channel))
                        await channel.Writer.WriteAsync(item, ct).ConfigureAwait(false);
                    // Items for unknown keys are silently dropped
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
