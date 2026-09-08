namespace ActualChat.UI.Blazor.App.Services.Gestures;

public static class HeadsetButtonPolicy
{
    public static HeadsetButtonState GetState(
        UserPttSettings settings,
        IReadOnlyList<ChatId> pttChatIds,
        IReadOnlyDictionary<ChatId, Moment> lastVoiceAt,
        Moment now,
        TimeSpan recencyWindow,
        bool isReplyHot,
        bool isPracticeMode)
    {
        // HasAnswerWindow, not ShouldSenseStartGestures: the latter also reports a window for
        // AreGesturesAlwaysOn and practice mode, which would arm the button with nobody talking.
        var hasAnswerWindow = GestureActivationPolicy.HasAnswerWindow(
            pttChatIds, lastVoiceAt, now, recencyWindow);
        return new(settings.IsHeadsetButtonEnabled ?? true, hasAnswerWindow, isReplyHot, isPracticeMode,
            pttChatIds.Count > 0);
    }

    public static HeadsetButtonAction Decide(
        HeadsetKey key,
        bool isDown,
        int repeatCount,
        bool isLongPress,
        bool isEnabled,
        bool hasAnswerWindow,
        bool isReplyHot,
        bool isPracticeMode,
        bool hasArmedChats)
    {
        if (!isDown || key == HeadsetKey.Unknown || !isEnabled)
            return HeadsetButtonAction.PassThrough;
        // Android flags exactly one of the auto-repeats as the long press; every other repeat is
        // still the same press and acting on it would open the mic and immediately close it.
        if (isLongPress)
            return hasArmedChats && !isPracticeMode ? HeadsetButtonAction.Hush : HeadsetButtonAction.PassThrough;
        if (repeatCount != 0)
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
    Hush,
}

public readonly record struct HeadsetButtonState(
    bool IsEnabled,
    bool HasAnswerWindow,
    bool IsReplyHot,
    bool IsPracticeMode,
    bool HasArmedChats);
