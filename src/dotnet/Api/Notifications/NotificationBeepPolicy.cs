namespace ActualChat.Notifications;

/// <summary>
/// Decides whether a push for a coalesced chat notification should audibly alert or update
/// silently. The first alert always fires; subsequent ones back off along
/// <see cref="Constants.Notification.BeepBackoff"/>, while unread mentions re-alert on a fixed
/// interval. A conversation lull resets the back-off at merge time (see
/// <see cref="ChatEntryRelatedNotification.MergeWith"/>), not here.
/// </summary>
public static class NotificationBeepPolicy
{
    public static bool ShouldBeep(NotificationKind kind, int beepCount, Moment lastBeepAt, Moment now)
    {
        if (beepCount <= 0)
            return true;

        var interval = kind is NotificationKind.Mention
            ? Constants.Notification.MentionReAlertInterval
            : GetBackoffInterval(beepCount);
        return now - lastBeepAt >= interval;
    }

    private static TimeSpan GetBackoffInterval(int beepCount)
    {
        var backoff = Constants.Notification.BeepBackoff;
        var index = Math.Min(beepCount, backoff.Length - 1);
        return backoff[index];
    }
}
