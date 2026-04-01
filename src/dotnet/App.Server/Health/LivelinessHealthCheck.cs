using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ActualChat.App.Server.Health;

/// <summary>
/// Liveliness health check. Always returns healthy.
/// CPU-based liveliness is an anti-pattern: high CPU means the process is working, not stuck.
/// The HTTP probe itself proves ASP.NET is responsive; DB connectivity is checked separately.
/// </summary>
public class LivelinessHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(HealthCheckResult.Healthy());
}
