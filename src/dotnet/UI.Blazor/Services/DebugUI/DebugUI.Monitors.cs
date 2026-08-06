using ActualChat.UI.Blazor.Diagnostics;
using ActualLab.Fusion.Diagnostics;

namespace ActualChat.UI.Blazor.Services;

// Diagnostic monitors that observe runtime state from JS console — typically
// long-running, write to the console / log without changing app behavior.
public sealed partial class DebugUI
{
    [JSInvokable]
    public void StartFusionMonitor()
    {
        var isServer = HostInfo.HostKind.IsServer();
        if (isServer)
            throw StandardError.Constraint("This method can be used only on WASM or MAUI client.");

        Services.GetRequiredService<FusionMonitor>().Start();
        Log.LogInformation("StartFusionMonitor: done");
    }

    [JSInvokable]
    public void StartTaskMonitor()
    {
        var isServer = HostInfo.HostKind.IsServer();
        if (isServer)
            throw StandardError.Constraint("This method can be used only on WASM or MAUI client.");

        Services.GetRequiredService<TaskMonitor>().Start();
        Services.GetRequiredService<TaskEventListener>().Start();
        Log.LogInformation("StartTaskMonitor: done");
    }
}
