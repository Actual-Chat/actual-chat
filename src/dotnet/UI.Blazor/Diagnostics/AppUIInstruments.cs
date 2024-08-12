using System.Diagnostics.Metrics;

// ReSharper disable once CheckNamespace
namespace ActualChat.Diagnostics;

public static class AppUIInstruments
{
    public static readonly ActivitySource ActivitySource = new("AppUI", ThisAssembly.AssemblyInformationalVersion);
    public static readonly Meter Meter = new("AppUI", ThisAssembly.AssemblyInformationalVersion);
}
