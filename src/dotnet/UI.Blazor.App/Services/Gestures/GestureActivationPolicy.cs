namespace ActualChat.UI.Blazor.App.Services.Gestures;

public static class GestureActivationPolicy
{
    public static bool HasAnswerWindow(
        IReadOnlyList<ChatId> pttChatIds,
        IReadOnlyDictionary<ChatId, Moment> lastVoiceAt,
        Moment now,
        TimeSpan recencyWindow)
        => GetAnswerWindowChat(pttChatIds, lastVoiceAt, now, recencyWindow) is not null;

    public static (ChatId ChatId, Moment At)? GetAnswerWindowChat(
        IReadOnlyList<ChatId> pttChatIds,
        IReadOnlyDictionary<ChatId, Moment> lastVoiceAt,
        Moment now,
        TimeSpan recencyWindow)
    {
        var since = now - recencyWindow;
        (ChatId ChatId, Moment At)? best = null;
        foreach (var chatId in pttChatIds) {
            if (!lastVoiceAt.TryGetValue(chatId, out var at) || at <= since)
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
        IReadOnlyDictionary<ChatId, Moment> lastVoiceAt,
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

        // Voice is the only thing that arms a start gesture. Opening the app deliberately does
        // not: a gesture surface that's live whenever the app is open is one an ordinary jostle
        // can fire, and the notification's Reply action already covers starting a conversation.
        return HasAnswerWindow(pttChatIds, lastVoiceAt, now, recencyWindow);
    }

    public static bool IsStartGestureReady(
        bool mustSenseStartGestures,
        bool isFlipToTalkEnabled,
        bool isDoubleShakeEnabled,
        bool isPracticeMode)
        // What the notification must promise: not "a chat is armed", but "a flip or shake right
        // now opens the mic". Sensing is only half of it - practice mode routes gestures away
        // from transmitting, and with both toggles off there is no start gesture to fire.
        => mustSenseStartGestures
            && !isPracticeMode
            && (isFlipToTalkEnabled || isDoubleShakeEnabled);

    public static bool ShouldSenseStopGesture(bool isStopGestureEnabled, bool isTransmitting, bool isPracticeMode)
    {
        // The playground must let the user rehearse the stop gesture even when the privacy
        // toggle is off; outside practice the toggle governs, and anything outgoing needs it:
        // an open mic, or a live camera/screencast stream.
        if (isPracticeMode)
            return true;

        return isStopGestureEnabled && isTransmitting;
    }

    public static bool ShouldSenseShake(
        bool isDoubleShakeEnabled,
        bool mustSenseStart,
        bool mustSenseStop,
        bool isMicOpen)
        // With the mic open a shake means "stop", so it rides the stop toggle, not shake-to-talk.
        // isMicOpen, not mustSenseStop: its video-only case would route a shake to StartReply.
        => (isDoubleShakeEnabled && mustSenseStart) || (mustSenseStop && isMicOpen);

    public static bool ShouldSenseHush(
        bool isHushGestureEnabled,
        bool isPracticeMode,
        bool hasArmedChats,
        bool hasLiveIncoming,
        bool hasAnswerWindow)
        // Practice never hushes: the detectors it rehearses are the stop-gesture ones, and a live
        // hush from the settings page would silently mute every chat. The window half keeps the
        // gesture available for the seconds after the utterance, while the user is still reacting.
        => isHushGestureEnabled && !isPracticeMode && hasArmedChats && (hasLiveIncoming || hasAnswerWindow);

    public static GestureRoute Route(
        GestureKind kind,
        bool isPracticeMode,
        bool isMicOpen,
        bool isStopArmed,
        bool isHushArmed)
    {
        // Practice never transmits: rehearsing a gesture in Settings must not open the mic.
        if (isPracticeMode)
            return kind == GestureKind.None ? GestureRoute.None : GestureRoute.Practice;

        return kind switch {
            // Stop sensing is on only while something outgoing is live (mic, camera, screencast),
            // and closing that always wins. With nothing outgoing, a face-down hushes the other
            // side; being pocketed is never a hush - the pat is, so a wake reaching a pocketed
            // phone doesn't hush itself.
            GestureKind.FaceDown or GestureKind.Pocket when isStopArmed => GestureRoute.StopReply,
            GestureKind.FaceDown => isHushArmed ? GestureRoute.Hush : GestureRoute.None,
            GestureKind.Pocket => GestureRoute.None,
            GestureKind.DoublePat => !isMicOpen && isHushArmed ? GestureRoute.Hush : GestureRoute.None,
            // The same shake means the opposite thing depending on the mic: nothing else can be
            // meant by shaking a phone that's already recording you.
            GestureKind.DoubleShake => isMicOpen ? GestureRoute.StopReply : GestureRoute.StartReply,
            GestureKind.FlipToTalk => GestureRoute.StartReply,
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
    Hush,
}
