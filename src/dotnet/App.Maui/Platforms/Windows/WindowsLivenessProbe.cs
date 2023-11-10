using ActualChat.App.Maui.Services;
using Microsoft.Win32;

namespace ActualChat.App.Maui;

public class WindowsLivenessProbe : MauiLivenessProbe
{
    public static void Activate()
    {
        var synchronizationContext = SynchronizationContext.Current;
        try {
            SynchronizationContext.SetSynchronizationContext(null);
            // NOTE(DF): Avoid capturing current SynchronizationContext during subscribing to SystemEvents.
            // SystemEvents uses Send method on captured SynchronizationContext to invoke event handlers.
            // Current SynchronizationContext on the MainThread is Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext.
            // It does not support Send method and invocation fails.
            SystemEvents.PowerModeChanged += static (_, e) => {
                if (e.Mode is PowerModes.Resume)
                    Check();
                else if (e.Mode is PowerModes.Suspend)
                    CancelCheck();
            };
            SystemEvents.SessionSwitch += static (_, e) => {
                if (e.Reason is SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
                    Check();
            };
        }
        finally {
            SynchronizationContext.SetSynchronizationContext(synchronizationContext);
        }
    }

    protected WindowsLivenessProbe(bool mustStart = true) : base(mustStart) { }
}
