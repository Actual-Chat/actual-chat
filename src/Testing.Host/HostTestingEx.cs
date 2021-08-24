using System;
using System.Linq;
using System.Net.Http;
using ActualChat.Host;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Testing
{
    public static class HostTestingEx
    {
        public static BlazorTester NewBlazorTester(this AppHost appHost)
            => new(appHost);

        public static HttpClient NewClient(this AppHost appHost)
            => new() { BaseAddress = new Uri(appHost.GetUrl()) };

        public static IServer Server(this IServiceProvider services)
            => services.GetRequiredService<IServer>();

        public static string GetUrl(this AppHost appHost)
            => appHost.Services.Server().GetUrl();

        public static string GetUrl(this IServer server)
            => server.Features.Get<IServerAddressesFeature>().Addresses.First();
    }
}
