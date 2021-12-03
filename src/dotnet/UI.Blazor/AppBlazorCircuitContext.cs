using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Blazor;

namespace ActualChat.UI.Blazor
{
    public sealed class AppBlazorCircuitContext : BlazorCircuitContext
    {
        private static long _lastId;

        private MomentClockSet Clocks { get; }
        private ILogger Log { get; }

        public long Id { get; }
        public IServiceProvider Services { get; }

        public AppBlazorCircuitContext(IServiceProvider services)
        {
            Id = Interlocked.Increment(ref _lastId);
            Services = services;
            Log = Services.LogFor(GetType());
            Clocks = Services.Clocks();
            Log.LogInformation("[+] Blazor Circuit #{Id}", Id);
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
                await Clocks.CpuClock.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                Log.LogDebug("DelayedDispose in Blazor Circuit #{Id}", Id);
                if (serviceScope is IAsyncDisposable ad) {
                    var __ = ad.DisposeAsync().ConfigureAwait(true);
                }
                else
                    serviceScope.Dispose();
            }
        }
    }
}
