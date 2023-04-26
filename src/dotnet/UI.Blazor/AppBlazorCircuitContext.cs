namespace ActualChat.UI.Blazor;

public sealed class AppBlazorCircuitContext : BlazorCircuitContext
{
    private static long _lastId;
    private readonly CancellationTokenSource _stopToken = new();

    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public IServiceProvider Services { get; }
    public long Id { get; }
    public string Origin { get; }
    public CancellationToken StopToken { get; }

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
            // We want it to use the same scheduler everywhere
            await Clocks.CpuClock.Delay(TimeSpan.FromSeconds(10));
            Log.LogDebug("DelayedDispose in Blazor Circuit #{Id}", Id);
            if (serviceScope is IAsyncDisposable ad) {
                var __ = ad.DisposeAsync();
            }
            else
                serviceScope.Dispose();
        }
    }
}
