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
