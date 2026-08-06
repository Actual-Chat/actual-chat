using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace ActualChat.App.Server.Module;

/// <summary>
/// Extension methods for mapping health check and Prometheus metrics endpoints.
/// </summary>
public static class EndpointsExt
{
    public const string HealthPathPrefix = "/health"; // NB: "/healthz" endpoint below matches this prefix too
    public const string PrometheusPathPrefix = "/metrics";
    public const string BackendPathPrefix = "/backend";

    public static IEndpointRouteBuilder MapAppHealth(this IEndpointRouteBuilder endpoints, params string[] tags)
    {
        foreach (var tag in new[] { HealthTags.Live, HealthTags.Ready })
            endpoints.MapHealthChecks(
                $"/healthz/{tag}", // NB: It starts with HealthPathPrefix too
                new HealthCheckOptions {
                    Predicate = healthCheck => healthCheck.Tags.Contains(tag),
                });

        endpoints.Map($"{HealthPathPrefix}/stop", StopServerEndpoint);
        return endpoints;
    }

    public static IEndpointRouteBuilder MapAppMetrics(this IEndpointRouteBuilder endpoints, params string[] tags)
    {
        var host = Environment.GetEnvironmentVariable("POD_IP") ?? Constants.Hosts.LocalVoxt;
        endpoints.MapPrometheusScrapingEndpoint().RequireHost("localhost", host);
        return endpoints;
    }

    // Private methods

    private static IResult StopServerEndpoint(HttpContext ctx, HostInfo hostInfo, IHostApplicationLifetime lifetime)
    {
        // Local-dev-only: allowed only when the request's Host header is local.voxt.ai (or equivalent).
        if (hostInfo is not { IsDevelopmentInstance: true, BaseUrlKind: BaseUrlKind.Local })
            return Results.NotFound();

        Console.WriteLine($"HTTP {HealthPathPrefix}/stop received - stopping the server...");
        lifetime.StopApplication();
        HardExit.Schedule(TimeSpan.FromSeconds(15),
            $"{HealthPathPrefix}/stop: graceful shutdown didn't finish within 15s");
        return Results.Text("stopping\n");
    }
}
