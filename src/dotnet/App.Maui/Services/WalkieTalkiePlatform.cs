using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// Platform hooks for <see cref="WalkieTalkieSession"/>: wake failure, playback start,
/// foreground-wake completion, and headless-session teardown.
/// </summary>
public abstract class WalkieTalkiePlatform
{
    public abstract void OnWakeFailed(ChatId chatId);
    public abstract void OnHeadlessTeardown();

    // Persisted across process restarts, so a transmit from a cold start still has an answer window.
    public virtual (ChatId ChatId, Moment At)? LastWake => null;

    public virtual Task OnPlaybackStarted(AppUIHub hub, ChatId chatId)
        => Task.CompletedTask;

    public virtual Task OnForegroundWakeHandled(ChatId chatId)
        => Task.CompletedTask;
}
