namespace ActualChat.UI.Blazor;

public sealed class AppBlazorCircuitContext : BlazorCircuitContext
{
    private static long _lastId;
    private ILogger? _log;

    private MomentClockSet Clocks { get; }
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public long Id { get; }
    public IServiceProvider Services { get; }

    public AppBlazorCircuitContext(IServiceProvider services)
    {
        Id = Interlocked.Increment(ref _lastId);
        Services = services;
        Clocks = services.Clocks();

        Log.LogInformation("[+] Blazor Circuit #{Id}", Id);
        services.GetRequiredService<UILifetimeEvents>().RaiseOnCircuitContextCreated(Services);
    }

    protected override void Dispose(bool disposing)
    {
        Log.LogInformation("[-] Blazor Circuit #{Id}", Id);
        if (Services is not IServiceScope serviceScope)
            return;
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
