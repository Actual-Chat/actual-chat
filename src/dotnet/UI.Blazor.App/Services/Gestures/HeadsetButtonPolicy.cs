namespace ActualChat.UI.Blazor.App.Services.Gestures;

public static class HeadsetButtonPolicy
{
    public static HeadsetButtonAction Decide(
        HeadsetKey key,
        bool isDown,
        int repeatCount,
        bool isEnabled,
        bool hasAnswerWindow,
        bool isReplyHot,
        bool isPracticeMode)
    {
        // One press delivers both edges plus auto-repeats; acting on more than one would
        // open the mic and immediately close it, because the later edges see a hot reply.
        if (!isDown || repeatCount != 0)
            return HeadsetButtonAction.PassThrough;
        if (!isEnabled || key == HeadsetKey.Unknown)
            return HeadsetButtonAction.PassThrough;
        // A reply can outlive both the answer window and the practice panel, so closing it
        // must depend on neither: leaving a live mic open is the unsafe direction.
        if (isReplyHot)
            return HeadsetButtonAction.StopReply;
        // ShouldSenseStartGestures reports a window unconditionally in practice mode, so
        // hasAnswerWindow is fabricated here - starting a reply would transmit for real.
        if (isPracticeMode)
            return HeadsetButtonAction.PassThrough;

        return hasAnswerWindow ? HeadsetButtonAction.StartReply : HeadsetButtonAction.PassThrough;
    }
}

public enum HeadsetKey
{
    Unknown = 0,
    Hook,
    PlayPause,
}

public enum HeadsetButtonAction
{
    PassThrough = 0,
    StartReply,
    StopReply,
}

public readonly record struct HeadsetButtonState(
    bool IsEnabled,
    bool HasAnswerWindow,
    bool IsReplyHot,
    bool IsPracticeMode);
