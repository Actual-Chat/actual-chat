namespace ActualChat.Async;

/// <summary>
/// Combines multiple Channel&lt;T&gt; sources into a single output channel.
/// Sets StreamIndex on each item based on its source.
/// </summary>
public sealed class ChannelMuxer<T> : IAsyncDisposable
    where T : IMuxable
{
    private readonly Channel<T> _output;
    private readonly ConcurrentDictionary<int, (ChannelReader<T> Reader, Task Task)> _sources = new();
    private readonly CancellationTokenSource _cts = new();
    private int _nextStreamIndex;
    private volatile bool _isDisposed;

    public ChannelReader<T> Output => _output.Reader;

    public ChannelMuxer(int? capacity = null)
        => _output = ChannelExt.Create<T>(capacity);

    /// <summary>
    /// Adds a source channel and returns its assigned stream index.
    /// </summary>
    public int AddSource(ChannelReader<T> source)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var streamIndex = Interlocked.Increment(ref _nextStreamIndex);
        var task = PumpAsync(source, streamIndex);
        _sources[streamIndex] = (source, task);
        return streamIndex;
    }

    public bool RemoveSource(int streamIndex)
        => _sources.TryRemove(streamIndex, out _);

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        await _cts.CancelAsync().ConfigureAwait(false);

        var tasks = _sources.Values.Select(x => x.Task).ToArray();
        if (tasks.Length > 0)
            await Task.WhenAll(tasks).SilentAwait(false);

        _output.Writer.TryComplete();
        _cts.Dispose();
    }

    // Private methods

    private async Task PumpAsync(ChannelReader<T> source, int streamIndex)
    {
        var ct = _cts.Token;
        try {
            while (await source.WaitToReadAsync(ct).ConfigureAwait(false))
                while (source.TryRead(out var item)) {
                    item.StreamIndex = streamIndex;
                    await _output.Writer.WriteAsync(item, ct).ConfigureAwait(false);
                }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            // Expected during disposal
        }
        finally {
            _sources.TryRemove(streamIndex, out _);
        }
    }
}
