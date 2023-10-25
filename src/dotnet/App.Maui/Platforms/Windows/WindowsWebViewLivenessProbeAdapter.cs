using ActualChat.App.Maui.Services;
using Microsoft.Win32;

namespace ActualChat.App.Maui;

public class WindowsWebViewLivenessProbeAdapter
{
    private MauiWebViewLivenessProbe? _probe;

    public void Subscribe()
    {
        var synchronizationContext = SynchronizationContext.Current;
        try {
            SynchronizationContext.SetSynchronizationContext(null);
            // NOTE(DF): Avoid capturing current SynchronizationContext during subscribing to SystemEvents.
            // SystemEvents uses Send method on captured SynchronizationContext to invoke event handler.
            // Current SynchronizationContext on the MainThread is Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext.
            // It does not support Send method and invocation fails.
            SystemEvents.PowerModeChanged += SystemEventsOnPowerModeChanged;
            SystemEvents.SessionSwitch += SystemEventsOnSessionSwitch;
        }
        finally {
            SynchronizationContext.SetSynchronizationContext(synchronizationContext);
        }
    }

    private void SystemEventsOnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            OnResume();
    }

    private void SystemEventsOnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.Resume)
            OnResume();
    }

    private void OnResume()
        => MainThread.BeginInvokeOnMainThread(() => {
            _probe?.StopCheck();
            _probe = new MauiWebViewLivenessProbe(MauiWinUIApplication.Current.Services);
            _ = _probe.StartCheck();
        });
}
