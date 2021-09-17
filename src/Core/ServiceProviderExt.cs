using System;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat
{
    public static class ServiceProviderExt
    {
        public static UriMapper UriMapper(this IServiceProvider services)
            => services.GetRequiredService<UriMapper>();
    }
}
