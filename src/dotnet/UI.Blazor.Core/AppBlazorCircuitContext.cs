using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Blazor;

namespace ActualChat.UI.Blazor
{
    public class AppBlazorCircuitContext : BlazorCircuitContext
    {
        public IServiceProvider Services { get; }

        public AppBlazorCircuitContext(IServiceProvider services)
            => Services = services;

        protected override void Dispose(bool disposing)
        {
            if (Services is not IServiceScope serviceScope)
                return;
            // Let's reliably dispose serviceScope
            Task.Delay(10_000).ContinueWith(_ => {
                if (serviceScope is IAsyncDisposable ad)
                    ad.DisposeAsync();
                else
                    serviceScope.Dispose();
            });
        }
    }
}
