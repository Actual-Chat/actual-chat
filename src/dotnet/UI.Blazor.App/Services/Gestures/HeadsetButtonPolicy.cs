namespace ActualChat.UI.Blazor.App.Services.Gestures;

public static class HeadsetButtonPolicy
{
    public static HeadsetButtonState GetState(
        UserPttSettings settings,
        IReadOnlyList<ChatId> pttChatIds,
        IReadOnlyDictionary<ChatId, Moment> lastIncomingVoiceAt,
        Moment now,
        TimeSpan recencyWindow,
        bool isReplyHot,
        bool isPracticeMode)
    {
        // HasAnswerWindow, not ShouldSenseStartGestures: the latter also reports a window for
        // AreGesturesAlwaysOn and practice mode, which would arm the button with nobody talking.
        var hasAnswerWindow = GestureActivationPolicy.HasAnswerWindow(
            pttChatIds, lastIncomingVoiceAt, now, recencyWindow);
        return new(settings.IsHeadsetButtonEnabled ?? true, hasAnswerWindow, isReplyHot, isPracticeMode);
    }

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
        // Rehearsing in the Settings practice panel must not transmit, whatever the window says.
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
