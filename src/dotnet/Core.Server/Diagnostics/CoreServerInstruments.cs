using System.Diagnostics.Metrics;

namespace ActualChat.Diagnostics;

public static class CoreServerInstruments
{
    public static readonly ActivitySource ActivitySource = new (ThisAssembly.AssemblyName, ThisAssembly.AssemblyInformationalVersion);
    public static readonly Meter Meter = new (ThisAssembly.AssemblyName, ThisAssembly.AssemblyInformationalVersion);
}
