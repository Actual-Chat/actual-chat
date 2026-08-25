namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Platform hooks for the PTT session: wake failure, playback start,
/// foreground-wake completion, and headless-session teardown.
/// </summary>
public abstract class PttPlatform
{
    public abstract void OnWakeFailed(ChatId chatId);
    public abstract void OnHeadlessTeardown();

    // False on hosts that can't tell. Android answers with the ringer mode and Do Not Disturb;
    // iOS can only answer for Focus, since the Ring/Silent switch has no public API.
    public virtual bool IsSilenced => false;

    // A wake this device won't act on: release what the wake path grabbed. Unlike OnWakeFailed,
    // the reason decides whether the user is told anything at all.
    public virtual void OnWakeIgnored(ChatId chatId, PttWakeIgnoreReason reason)
        => OnHeadlessTeardown();

    // Persisted across process restarts, so a transmit from a cold start still has an answer window.
    public virtual (ChatId ChatId, Moment At)? LastWake => null;

    public virtual Task OnPlaybackStarted(AppUIHub hub, ChatId chatId)
        => Task.CompletedTask;

    public virtual Task OnForegroundWakeHandled(ChatId chatId)
        => Task.CompletedTask;
}

public enum PttWakeIgnoreReason
{
    DeviceDisabled,
    Silenced,
}
