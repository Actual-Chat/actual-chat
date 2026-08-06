using ActualChat.Maui.Services;

namespace ActualChat.App.Maui.Services;

// Must be singleton!
public sealed class MauiBackgroundStateTracker : BackgroundStateTracker
{
    public override IState<bool> IsBackground => MauiBackgroundState.IsBackground;
}
