namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Platform hooks for the PTT session: wake failure, playback start,
/// foreground-wake completion, and headless-session teardown.
/// </summary>
public abstract class PttPlatform
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
