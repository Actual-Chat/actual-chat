namespace ActualChat.UI.Blazor.App.Services.Gestures;

public static class GestureActivationPolicy
{
    public static bool ShouldSenseStartGestures(
        bool areGesturesAlwaysOn,
        bool isPracticeMode,
        IReadOnlyList<ChatId> pttChatIds,
        IReadOnlyDictionary<ChatId, Moment> lastIncomingVoiceAt,
        Moment now,
        TimeSpan recencyWindow)
    {
        if (isPracticeMode)
            return true;
        if (pttChatIds.Count == 0)
            return false;
        if (areGesturesAlwaysOn)
            return true;

        var since = now - recencyWindow;
        foreach (var chatId in pttChatIds)
            if (lastIncomingVoiceAt.TryGetValue(chatId, out var at) && at > since)
                return true;

        return false;
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
