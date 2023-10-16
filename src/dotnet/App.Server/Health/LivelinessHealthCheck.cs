using ActualChat.App.Server.Module;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ActualChat.App.Server.Health;

public class LivelinessHealthCheck(IServiceProvider services): IHealthCheck
{
    private const double CpuUsageLimit = 70;
    private IRuntimeStats RuntimeStats { get; } = services.GetRequiredService<IRuntimeStats>();
    private HostSettings HostSettings { get; } = services.GetRequiredService<HostSettings>();

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = new ())
        => Task.FromResult(RuntimeStats.CpuMean20.Value >  (HostSettings.LivelinessCpuLimit ?? CpuUsageLimit)
            ? HealthCheckResult.Unhealthy("Continuous critical CPU usage - requires restart.")
            : HealthCheckResult.Healthy());
}
