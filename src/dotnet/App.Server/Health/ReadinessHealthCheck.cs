using ActualChat.App.Server.Module;
using ActualChat.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ActualChat.App.Server.Health;

/// <summary>
/// Checks if the server is ready to accept new requests based on CPU usage.
/// The threshold is normalized by processor count because the cpu-usage EventCounter
/// reports percentage per single core (e.g., 2-core usage = 200%).
/// </summary>
public class ReadinessHealthCheck(IServiceProvider services): IHealthCheck
{
    private const double CpuUsageLimitPerCore = 70;
    private IHealthState HealthState { get; } = services.GetRequiredService<IHealthState>();
    private HostSettings HostSettings { get; } = services.GetRequiredService<HostSettings>();

    private double EffectiveLimit
        => (HostSettings.ReadinessCpuLimit ?? CpuUsageLimitPerCore) * Environment.ProcessorCount;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(HealthState.CpuMean5.Value > EffectiveLimit
            ? HealthCheckResult.Unhealthy($"CPU usage {HealthState.CpuMean5.Value:F0}% exceeds limit {EffectiveLimit:F0}% ({Environment.ProcessorCount} cores).")
            : HealthCheckResult.Healthy());
}
