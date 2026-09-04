namespace ActualChat.Notifications;

/// <summary>
/// Decides whether a push for a coalesced chat notification should audibly alert or update
/// silently. Spoken messages alert once per voice context and then no more often than
/// <see cref="Constants.Notification.VoiceReAlertInterval"/>; typed ones back off along
/// <see cref="Constants.Notification.BeepBackoff"/>, and unread mentions re-alert on a fixed
/// interval. A conversation lull resets both (see <see cref="ChatEntryRelatedNotification.MergeWith"/>).
/// </summary>
public static class NotificationBeepPolicy
{
    public static bool ShouldBeep(ChatEntryRelatedNotification notification, Moment now)
    {
        // A spoken message alerts when its voice context changes - a new speaker taking over - and
        // otherwise only once per interval. BeepCount plays no part: a monologue would walk up the
        // back-off and land on the same 30min tail whether it ran for two minutes or two hours.
        var beepGroup = notification.BeepGroup;
        if (beepGroup.IsNullOrEmpty())
            return ShouldBeep(notification.Kind, notification.BeepCount, notification.LastBeepAt, now);

        return beepGroup != notification.LastBeepGroup
            || now - notification.LastBeepAt >= Constants.Notification.VoiceReAlertInterval;
    }

    public static ChatEntryRelatedNotification MarkBeeped(ChatEntryRelatedNotification notification, Moment now)
        => notification with {
            BeepCount = notification.BeepCount + 1,
            LastBeepAt = now,
            // A typed message alerts on its own back-off, but it must not erase the speaker run it
            // interrupts - the next utterance from that speaker would read as a handover and alert
            // again, well inside VoiceReAlertInterval.
            LastBeepGroup = notification.BeepGroup.IsNullOrEmpty()
                ? notification.LastBeepGroup
                : notification.BeepGroup,
        };

    // Spoken messages measure the lull against the longer voice interval: at BeepResetPeriod an
    // ordinary pause mid-monologue would re-arm the beep on the very next utterance.
    public static TimeSpan GetLullPeriod(string? beepGroup)
        => beepGroup.IsNullOrEmpty()
            ? Constants.Notification.BeepResetPeriod
            : Constants.Notification.VoiceReAlertInterval;

    public static BeepMemory Remember(ChatEntryRelatedNotification removed)
        => new(removed.Id, removed.SentAt, removed.BeepCount, removed.LastBeepAt,
            removed.BeepGroup ?? "", removed.LastBeepGroup ?? "");

    // Seeds a fresh notification with the beep state its removed predecessor left behind, under
    // the same lull and speaker-change rules MergeWith applies to a live one.
    public static ChatEntryRelatedNotification Inherit(ChatEntryRelatedNotification fresh, BeepMemory memory)
    {
        var beepGroup = fresh.BeepGroup ?? "";
        if (fresh.SentAt - memory.SentAt >= GetLullPeriod(beepGroup))
            return fresh;

        var isSpeakerChanged = !beepGroup.IsNullOrEmpty() && beepGroup != memory.BeepGroup;
        return fresh with {
            BeepCount = memory.BeepCount,
            LastBeepAt = memory.LastBeepAt,
            LastBeepGroup = isSpeakerChanged ? "" : memory.LastBeepGroup,
        };
    }

    public static bool ShouldBeep(NotificationKind kind, int beepCount, Moment lastBeepAt, Moment now)
    {
        if (beepCount <= 0)
            return true;

        var interval = kind is NotificationKind.Mention
            ? Constants.Notification.MentionReAlertInterval
            : GetBackoffInterval(beepCount);
        return now - lastBeepAt >= interval;
    }

    // Private methods

    private static TimeSpan GetBackoffInterval(int beepCount)
    {
        var backoff = Constants.Notification.BeepBackoff;
        var index = Math.Min(beepCount, backoff.Length - 1);
        return backoff[index];
    }
}
