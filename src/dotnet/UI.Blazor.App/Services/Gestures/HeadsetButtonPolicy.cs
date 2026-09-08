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
        bool isLongPress,
        bool wasLongPressHandled,
        bool isEnabled,
        bool hasAnswerWindow,
        bool isReplyHot,
        bool isPracticeMode,
        bool hasArmedChats)
    {
        if (key == HeadsetKey.Unknown || !isEnabled)
            return HeadsetButtonAction.PassThrough;

        // A short press acts on its release, not its first edge: Android delivers that edge before
        // it knows whether the press will turn out long, and acting on it would open the mic half a
        // second before the long press hushes. A press this policy owns swallows every edge, so the
        // system's own play/pause handling never sees half of it.
        var mayHush = hasArmedChats && !isPracticeMode;
        var mayReply = isReplyHot || (!isPracticeMode && hasAnswerWindow);
        if (!mayHush && !mayReply)
            return HeadsetButtonAction.PassThrough;
        if (isDown)
            return isLongPress && mayHush ? HeadsetButtonAction.Hush : HeadsetButtonAction.Consume;
        if (wasLongPressHandled)
            return HeadsetButtonAction.Consume;
        // A reply can outlive both the answer window and the practice panel, so closing it
        // must depend on neither: leaving a live mic open is the unsafe direction.
        if (isReplyHot)
            return HeadsetButtonAction.StopReply;
        // Rehearsing in the Settings practice panel must not transmit, whatever the window says.
        if (isPracticeMode)
            return HeadsetButtonAction.Consume;

        return hasAnswerWindow ? HeadsetButtonAction.StartReply : HeadsetButtonAction.Consume;
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
    // Swallowed without acting: an edge of a press this policy owns whose action lands on another edge.
    Consume,
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
