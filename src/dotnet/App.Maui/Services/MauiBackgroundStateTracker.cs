using ActualChat.Maui.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

// Must be singleton!
public sealed class MauiBackgroundStateTracker : BackgroundStateTracker
{
    public override IState<bool> IsBackground => MauiBackgroundState.IsBackground;
}
