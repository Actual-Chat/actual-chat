using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace ActualChat.Authentication;

public static class EndpointRouteBuilderExt
{
    public static IEndpointRouteBuilder MapFusionAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var services = endpoints.ServiceProvider;
        var handler = services.GetRequiredService<AuthEndpoints>();
        endpoints
            .MapGet("/signIn", handler.SignIn)
            .WithGroupName("FusionAuth");
        endpoints
            .MapGet("/signIn/{scheme}", handler.SignIn)
            .WithGroupName("FusionAuth");
        endpoints
            .MapGet("/signOut", handler.SignOut)
            .WithGroupName("FusionAuth");
        return endpoints;
    }
}
