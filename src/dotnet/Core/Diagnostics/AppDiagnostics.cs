using System.Diagnostics.Metrics;

namespace ActualChat.Diagnostics;

public static class AppDiagnostics
{
    public static readonly ActivitySource AppTrace = new("App", typeof(AppDiagnostics).Assembly.GetInformationalVersion());
    public static readonly ActivitySource BlazorUITrace = new("BlazorUI", typeof(AppDiagnostics).Assembly.GetInformationalVersion());
    public static readonly Meter AppMeter = new("App", typeof(AppDiagnostics).Assembly.GetInformationalVersion());
}
