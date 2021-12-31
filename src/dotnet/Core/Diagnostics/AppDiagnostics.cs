using System.Diagnostics.Metrics;

namespace ActualChat.Diagnostics;

public static class AppDiagnostics
{
    public static ActivitySource AppTrace { get; } = new("ActualChat.App", ThisAssembly.AssemblyInformationalVersion);
    public static Meter AppMeter { get; } = new("ActualChat.App", ThisAssembly.AssemblyInformationalVersion);
}
