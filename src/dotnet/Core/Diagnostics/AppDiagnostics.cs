using System.Diagnostics.Metrics;

namespace ActualChat.Diagnostics;

public static class AppDiagnostics
{
    public static ActivitySource AppTrace { get; } = new("App", typeof(AppDiagnostics).Assembly.GetInformationalVersion());
    public static ActivitySource BlazorUITrace { get; } = new("BlazorUI", typeof(AppDiagnostics).Assembly.GetInformationalVersion());
    public static Meter AppMeter { get; } = new("App", typeof(AppDiagnostics).Assembly.GetInformationalVersion());
}
