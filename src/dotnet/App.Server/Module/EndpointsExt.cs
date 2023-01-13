using ActualChat.Hosting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
namespace ActualChat.App.Server.Module;

public static class EndpointsExt
{
    public const string HealthPathPrefix = "/health";
    public const string PrometheusPathPrefix = "/metrics";

    public static IEndpointRouteBuilder MapAppHealth(this IEndpointRouteBuilder endpoints, params string[] tags)
    {
        foreach (var tag in new[] { HealthTags.Live, HealthTags.Ready })
            endpoints.MapHealthChecks($"/healthz/{tag}",
                    new HealthCheckOptions {
                        Predicate = healthCheck => healthCheck.Tags.Contains(tag),
                    })
                .RequireHost("localhost");

        return endpoints;
    }

    public static IEndpointRouteBuilder MapAppMetrics(this IEndpointRouteBuilder endpoints, params string[] tags)
    {
        endpoints.MapPrometheusScrapingEndpoint()
            .RequireHost("localhost");

        return endpoints;
    }
}
