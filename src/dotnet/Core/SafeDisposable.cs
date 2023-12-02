namespace ActualChat;

public class SafeDisposable(object disposable, TimeSpan timeout, ILogger? log = null)
    : IDisposable, IAsyncDisposable
{
    private volatile int _isDisposed;

    public bool MustWait { get; init; } = true;

    public SafeDisposable(object disposable, double timeoutSeconds, ILogger? log = null)
        : this(disposable, TimeSpan.FromSeconds(timeoutSeconds), log) { }

    public void Dispose()
        => _ = DisposeAsync();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0)
            return;

        GC.SuppressFinalize(this);
        var disposeTask = BackgroundTask.Run(async () => {
            if (disposable is IAsyncDisposable ad)
                await ad.DisposeAsync().ConfigureAwait(false);
            else if (disposable is IDisposable d)
                d.Dispose();
        });

        var resultTask = BackgroundTask.Run(async () => {
            using var cts = new CancellationTokenSource(timeout);
            var cancellationToken = cts.Token;
            try {
                await disposeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) {
                if (cancellationToken.IsCancellationRequested)
                    log?.LogError(e, "Background dispose is timed out ({Timeout})", timeout.ToShortString());
                else
                    log?.LogError(e, "Background dispose failed");
            }
        });

        if (MustWait)
            await resultTask.ConfigureAwait(false);
    }
}
