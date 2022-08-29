namespace ActualChat;

public class SafeDisposable : IDisposable, IAsyncDisposable
{
    private readonly object _disposable;
    private readonly TimeSpan _timeout;
    private readonly ILogger? _log;
    private volatile int _isDisposed;

    public bool MustWait { get; init; } = true;

    public SafeDisposable(object disposable, double timeoutSeconds, ILogger? log = null)
        : this(disposable, TimeSpan.FromSeconds(timeoutSeconds), log) { }
    public SafeDisposable(object disposable, TimeSpan timeout, ILogger? log = null)
    {
        _disposable = disposable;
        _timeout = timeout;
        _log = log;
    }

    public void Dispose()
        => _ = DisposeAsync();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0)
            return;

        var disposeTask = BackgroundTask.Run(async () => {
            if (_disposable is IAsyncDisposable ad)
                await ad.DisposeAsync().ConfigureAwait(false);
            else if (_disposable is IDisposable d)
                d.Dispose();
        });

        var resultTask = BackgroundTask.Run(async () => {
            using var cts = new CancellationTokenSource(_timeout);
            var cancellationToken = cts.Token;
            try {
                await disposeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) {
                if (cancellationToken.IsCancellationRequested)
                    _log?.LogError(e, "Background dispose is timed out ({Timeout})", _timeout.ToShortString());
                else
                    _log?.LogError(e, "Background dispose failed");
            }
        });

        if (MustWait)
            await resultTask.ConfigureAwait(false);
    }
}
