
using System.Diagnostics.Metrics;

namespace ActualChat.MLSearch.Diagnostics;

public static class MLSearchInstruments
{
    public static readonly ActivitySource ActivitySource = new(ThisAssembly.AssemblyName, ThisAssembly.AssemblyInformationalVersion);
    public static readonly Meter Meter = new(ThisAssembly.AssemblyName, ThisAssembly.AssemblyInformationalVersion);
}
