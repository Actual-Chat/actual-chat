using System;
using System.Net.Http;
using ActualChat.Host;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Testing
{
    public static class TestHttpClientExt
    {
        public static HttpClient NewClient(this AppHost appHost)
        {
            var uriMapper = appHost.Services.UriMapper();
            return new() { BaseAddress = uriMapper.BaseUri };
        }

        public static IServer Server(this IServiceProvider services)
            => services.GetRequiredService<IServer>();
    }
}
