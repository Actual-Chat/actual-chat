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

        return HasAnswerWindow(pttChatIds, lastIncomingVoiceAt, now, recencyWindow);
    }

    public static bool ShouldSenseStopGesture(bool isFaceDownStopEnabled, bool isMicOpen, bool isPracticeMode)
    {
        // The playground must let the user rehearse the stop gesture even when the privacy
        // toggle is off; outside practice the toggle governs and only an open mic needs it.
        if (isPracticeMode)
            return true;

        return isFaceDownStopEnabled && isMicOpen;
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
