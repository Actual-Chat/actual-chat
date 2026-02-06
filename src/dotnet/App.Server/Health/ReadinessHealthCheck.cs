using ActualChat.App.Server.Module;
using ActualChat.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ActualChat.App.Server.Health;

/// <summary>
/// Checks if the server is ready to accept new requests based on CPU usage.
/// </summary>
public class ReadinessHealthCheck(IServiceProvider services): IHealthCheck
{
    private const double CpuUsageLimit = 70;
    private IHealthState HealthState { get; } = services.GetRequiredService<IHealthState>();
    private HostSettings HostSettings { get; } = services.GetRequiredService<HostSettings>();

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = new ())
        => Task.FromResult(HealthState.CpuMean5.Value > (HostSettings.ReadinessCpuLimit ?? CpuUsageLimit)
            ? HealthCheckResult.Unhealthy("CPU usage is too high to serve new request.")
            : HealthCheckResult.Healthy());
}
