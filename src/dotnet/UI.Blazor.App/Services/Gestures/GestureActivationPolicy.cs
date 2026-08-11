namespace ActualChat.UI.Blazor.App.Services.Gestures;

public static class GestureActivationPolicy
{
    public static bool HasAnswerWindow(
        IReadOnlyList<ChatId> pttChatIds,
        IReadOnlyDictionary<ChatId, Moment> lastIncomingVoiceAt,
        Moment now,
        TimeSpan recencyWindow)
        => GetAnswerWindowChat(pttChatIds, lastIncomingVoiceAt, now, recencyWindow) is not null;

    public static (ChatId ChatId, Moment At)? GetAnswerWindowChat(
        IReadOnlyList<ChatId> pttChatIds,
        IReadOnlyDictionary<ChatId, Moment> lastIncomingVoiceAt,
        Moment now,
        TimeSpan recencyWindow)
    {
        var since = now - recencyWindow;
        (ChatId ChatId, Moment At)? best = null;
        foreach (var chatId in pttChatIds) {
            if (!lastIncomingVoiceAt.TryGetValue(chatId, out var at) || at <= since)
                continue;
            if (best is not { } vBest || at > vBest.At)
                best = (chatId, at);
        }
        return best;
    }

    public static bool ShouldSenseStartGestures(
        bool areGesturesAlwaysOn,
        bool isPracticeMode,
        TimeSpan sinceForegrounded,
        IReadOnlyList<ChatId> pttChatIds,
        IReadOnlyDictionary<ChatId, Moment> lastIncomingVoiceAt,
        Moment now,
        TimeSpan recencyWindow)
    {
        // Practice mode and "always on" are properties of the sensors, not of the answer window:
        // a consumer that must not open the mic on its own has to ask HasAnswerWindow instead.
        if (isPracticeMode)
            return true;
        if (pttChatIds.Count == 0)
            return false;
        if (areGesturesAlwaysOn)
            return true;
        // Opening the app arms the gestures for the same duration an incoming voice does, so
        // "open and shake" works without waiting for the other side to speak first. Elapsed
        // awake-time (CpuClock domain), not a ServerClock stamp: the post-resume time resync
        // would jump a stamp-based check past the window in one tick.
        if (sinceForegrounded <= recencyWindow)
            return true;

        return HasAnswerWindow(pttChatIds, lastIncomingVoiceAt, now, recencyWindow);
    }

    public static bool ShouldSenseStopGesture(bool isFaceDownStopEnabled, bool isTransmitting, bool isPracticeMode)
    {
        // The playground must let the user rehearse the stop gesture even when the privacy
        // toggle is off; outside practice the toggle governs, and anything outgoing needs it:
        // an open mic, or a live camera/screencast stream.
        if (isPracticeMode)
            return true;

        return isFaceDownStopEnabled && isTransmitting;
    }

    public static GestureRoute Route(GestureKind kind, bool isPracticeMode)
    {
        // Practice never transmits: rehearsing a gesture in Settings must not open the mic.
        if (isPracticeMode)
            return kind == GestureKind.None ? GestureRoute.None : GestureRoute.Practice;

        return kind switch {
            GestureKind.FaceDown => GestureRoute.StopReply,
            GestureKind.FlipToTalk or GestureKind.DoubleShake => GestureRoute.StartReply,
            _ => GestureRoute.None,
        };
    }
}

public enum GestureRoute
{
    None = 0,
    Practice,
    StartReply,
    StopReply,
}
