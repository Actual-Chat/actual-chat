using Microsoft.AspNetCore.Routing;

namespace ActualChat.Distribution.Module
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