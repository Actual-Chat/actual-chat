using ActualChat.App.Server.Module;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ActualChat.App.Server.Health;

public class ReadinessHealthCheck(IServiceProvider services): IHealthCheck
{
    private const double CpuUsageLimit = 70;
    private IRuntimeStats RuntimeStats { get; } = services.GetRequiredService<IRuntimeStats>();
    private HostSettings HostSettings { get; } = services.GetRequiredService<HostSettings>();

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = new ())
        => Task.FromResult(RuntimeStats.CpuMean5.Value > (HostSettings.ReadinessCpuLimit ?? CpuUsageLimit)
            ? HealthCheckResult.Unhealthy("CPU usage is too high to serve new request.")
            : HealthCheckResult.Healthy());
}
