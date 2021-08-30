using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using ActualChat.Testing;
using Microsoft.Extensions.Hosting;
using Xunit;
using Microsoft.Playwright;

namespace ActualChat.Tests.UI.Blazor
{
    public class WebServerFixture : IAsyncLifetime, IDisposable
    {
        private readonly IHost _host;
        private IPlaywright? Playwright { get; set; }
        public IBrowser? Browser { get; private set; }
        public string BaseUrl { get; }

        public WebServerFixture()
        {
            var appHost = TestHostFactory.NewAppHost().Result;
            BaseUrl = appHost.ServerUrls;
            _host = appHost.Host;
        }

        public async Task InitializeAsync()
        {
            Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions{ Headless = true });
        }

        public async Task DisposeAsync()
        {
            await _host.StopAsync();
            _host?.Dispose();
            Playwright?.Dispose();
        }

        public void Dispose()
        {
            _host?.Dispose();
            Playwright?.Dispose();
        }
        
        private static int GetRandomUnusedPort()
        {
            var listener = new TcpListener(IPAddress.Any, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
