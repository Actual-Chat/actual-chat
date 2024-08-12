using ActualChat.App.Server.Module;
using ActualChat.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ActualChat.App.Server.Health;

public class LivelinessHealthCheck(IServiceProvider services): IHealthCheck
{
    private const double CpuUsageLimit = 70;
    private IHealthState HealthState { get; } = services.GetRequiredService<IHealthState>();
    private HostSettings HostSettings { get; } = services.GetRequiredService<HostSettings>();

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = new ())
        => Task.FromResult(HealthState.CpuMean20.Value >  (HostSettings.LivelinessCpuLimit ?? CpuUsageLimit)
            ? HealthCheckResult.Unhealthy("Continuous critical CPU usage - requires restart.")
            : HealthCheckResult.Healthy());
}
