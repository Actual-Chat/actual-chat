namespace ActualChat.UI.Blazor;

public sealed class AppBlazorCircuitContext : BlazorCircuitContext
{
    private static long _lastId;
    private readonly CancellationTokenSource _stopToken = new();
    private readonly TaskCompletionSource _whenReady = TaskCompletionSourceExt.New();

    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public IServiceProvider Services { get; }
    public long Id { get; }
    public string Origin { get; }
    public CancellationToken StopToken { get; }
    public Task WhenReady => _whenReady.Task;

    public AppBlazorCircuitContext(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();

        Id = Interlocked.Increment(ref _lastId);
        Origin = Alphabet.AlphaNumeric.Generator8.Next();
        StopToken = _stopToken.Token;
        Log.LogInformation("[+] Blazor Circuit #{Id}", Id);
    }

    public void MarkReady()
        => _whenReady.TrySetResult();

    protected override void Dispose(bool disposing)
    {
        Log.LogInformation("[-] Blazor Circuit #{Id}", Id);
        if (Services is not IServiceScope serviceScope)
            return;
        if (StopToken.IsCancellationRequested)
            return;
        _stopToken.CancelAndDisposeSilently();

        // Let's reliably dispose serviceScope
        var _ = DelayedDispose()
            .WithErrorLog(Log, "Delayed dispose of AppBlazorCircuitContext's service scope failed");

        async Task DelayedDispose()
        {
            // We want it to use the same scheduler everywhere.
            // Don't pass StopToken cause this code runs only when it's already cancelled!
            await Clocks.CpuClock.Delay(TimeSpan.FromSeconds(10), CancellationToken.None).ConfigureAwait(true);
            Log.LogDebug("DelayedDispose in Blazor Circuit #{Id}", Id);
            if (serviceScope is IAsyncDisposable ad) {
                var __ = ad.DisposeAsync();
            }
            else
                serviceScope.Dispose();
        }
    }
}
