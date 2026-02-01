namespace ActualChat.Async;

/// <summary>
/// Combines multiple Channel&lt;T&gt; sources into a single output channel.
/// Supports dynamic add/remove of sources.
/// </summary>
public sealed class ChannelMuxer<T> : IAsyncDisposable
{
    private readonly Channel<T> _output;
    private readonly ConcurrentDictionary<ChannelReader<T>, Task> _readers = new();
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _isDisposed;

    public ChannelReader<T> Output => _output.Reader;

    public ChannelMuxer(int? capacity = null)
    {
        _output = capacity.HasValue
            ? Channel.CreateBounded<T>(new BoundedChannelOptions(capacity.Value) {
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            })
            : Channel.CreateUnbounded<T>(new UnboundedChannelOptions {
                SingleReader = false,
                SingleWriter = false,
            });
    }

    public void AddSource(ChannelReader<T> source)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var task = PumpAsync(source);
        _readers.TryAdd(source, task);
    }

    public void RemoveSource(ChannelReader<T> source)
        => _readers.TryRemove(source, out _);

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        await _cts.CancelAsync().ConfigureAwait(false);

        // Wait for all pumps to complete
        var tasks = _readers.Values.ToArray();
        if (tasks.Length > 0)
            await Task.WhenAll(tasks).SilentAwait(false);

        _output.Writer.TryComplete();
        _cts.Dispose();
    }

    // Private methods

    private async Task PumpAsync(ChannelReader<T> source)
    {
        var ct = _cts.Token;
        try {
            while (await source.WaitToReadAsync(ct).ConfigureAwait(false))
                while (source.TryRead(out var item))
                    await _output.Writer.WriteAsync(item, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            // Expected during disposal
        }
        finally {
            _readers.TryRemove(source, out _);
        }
    }
}
