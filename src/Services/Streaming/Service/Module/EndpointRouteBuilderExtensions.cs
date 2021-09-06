using Microsoft.AspNetCore.Routing;

namespace ActualChat.Streaming.Module
{
    public static class EndpointRouteBuilderExtensions
    {
        public static IEndpointRouteBuilder MapHubs(this IEndpointRouteBuilder builder, IHubRegistrar registrar)
        {
            registrar.RegisterHubs(builder);
            return builder;
        }
    }
}