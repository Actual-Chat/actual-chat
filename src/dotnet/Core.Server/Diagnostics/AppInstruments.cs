using System.Diagnostics.Metrics;

// ReSharper disable once CheckNamespace
namespace ActualChat.Diagnostics;

public static class AppInstruments
{
    public static readonly ActivitySource ActivitySource = new("App", ThisAssembly.AssemblyInformationalVersion);
    public static readonly Meter Meter = new("App", ThisAssembly.AssemblyInformationalVersion);
}
