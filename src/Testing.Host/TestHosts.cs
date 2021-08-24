using System;
using System.Threading.Tasks;
using ActualChat.Host;
using Stl.Testing;

namespace ActualChat.Testing
{
    public static class TestHosts
    {
        public static async Task<AppHost> NewAppHost(Action<AppHost>? configure = null)
        {
            var appHost = new AppHost() {
                ServerUrls = WebTestEx.GetRandomLocalUri().ToString()
            };
            configure?.Invoke(appHost);
            await appHost.Build();
            await appHost.Initialize(true);
            await appHost.Start();
            return appHost;
        }
    }
}
