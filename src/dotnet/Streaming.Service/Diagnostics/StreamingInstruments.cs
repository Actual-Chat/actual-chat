using System.Diagnostics.Metrics;

namespace ActualChat.Streaming.Diagnostics;

public static class StreamingInstruments
{
    public static readonly ActivitySource ActivitySource = new(ThisAssembly.AssemblyName, ThisAssembly.AssemblyInformationalVersion);
    public static readonly Meter Meter = new(ThisAssembly.AssemblyName, ThisAssembly.AssemblyInformationalVersion);
}
