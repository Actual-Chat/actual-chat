using ActualChat.Maui.Services;

namespace ActualChat.App.Maui.Services;

// Must be singleton!
public sealed class MauiBackgroundStateTracker : BackgroundStateTracker
{
    public override IState<bool> IsBackground => MauiBackgroundState.IsBackground;
#if WINDOWS || MACOS || LINUX
    public override IState<bool> IsHidden => MauiBackgroundState.IsHidden;
#else
    // A phone app is suspendable exactly when its UI is out of sight, and the OS lifecycle
    // flag is the verified signal there, so the same state serves both
    public override IState<bool> IsHidden => MauiBackgroundState.IsBackground;
#endif
}
