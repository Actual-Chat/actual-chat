using System.Diagnostics.Metrics;

namespace ActualChat.Db.Diagnostics;

public static class DbInstruments
{
    public static readonly ActivitySource ActivitySource = new (ThisAssembly.AssemblyName, ThisAssembly.AssemblyInformationalVersion);
    public static readonly Meter Meter = new (ThisAssembly.AssemblyName, ThisAssembly.AssemblyInformationalVersion);
}
