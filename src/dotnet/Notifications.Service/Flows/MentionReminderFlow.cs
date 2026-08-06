using ActualChat.Flows;
using ActualChat.Queues;

namespace ActualChat.Notifications.Flows;

/// <summary>
/// Re-alerts a user about their unread @mentions on a fixed interval, at most
/// <see cref="Constants.Notification.MaxMentionReAlerts"/> times per mention. One flow per user,
/// keyed by user id; started when a mention is delivered, stops once none remain unread or every
/// remaining mention has exhausted its re-alerts.
/// </summary>
[Flow(DelayQuanta = 60)]
[DataContract, MessagePackObject(true)]
public partial class MentionReminderFlow : PeriodicFlow
{
    // Persisted state. Orders continue after PeriodicFlow's 0..2 with a gap for future base state.
    [DataMember(Order = 10), Key(10)]
    public Dictionary<string, int> ReAlertCounts { get; set; } = new();

    public static bool IsDue(Notification mention, Moment now)
        => now - mention.SentAt >= Constants.Notification.MentionReAlertInterval;

    public static bool ShouldReAlert(Notification mention, Moment now, int reAlertCount)
        => reAlertCount < Constants.Notification.MaxMentionReAlerts && IsDue(mention, now);

    protected override async ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken)
    {
        var userId = UserId.Parse(Id.Arguments);
        var accounts = Services.GetRequiredService<IAccountsBackend>();
        var account = await accounts.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account?.IsGuestOrNull() != false)
            return "No account";

        return FlowReadiness.Ready;
    }

    protected override async ValueTask<Moment> Run(CancellationToken cancellationToken)
    {
        var userId = UserId.Parse(Id.Arguments);
        var backend = Services.GetRequiredService<INotificationsBackend>();
        var info = await backend.GetUserNotificationInfo(userId, cancellationToken).ConfigureAwait(false);
        var mentions = info.Displayed.Where(n => n.Kind == NotificationKind.Mention).ToList();
        if (mentions.Count == 0) {
            ReAlertCounts.Clear();
            return Moment.MaxValue; // nothing unread -> stop until a new mention restarts the flow
        }

        var mentionIds = mentions.Select(m => m.Id.Value).ToHashSet();
        foreach (var staleId in ReAlertCounts.Keys.Where(id => !mentionIds.Contains(id)).ToList())
            ReAlertCounts.Remove(staleId);

        var now = Hub.SystemNow;
        var queues = Services.Queues();
        var hasMore = false;
        foreach (var mention in mentions) {
            var id = mention.Id.Value;
            var count = ReAlertCounts.GetValueOrDefault(id);
            if (ShouldReAlert(mention, now, count)) {
                await queues.Enqueue(new NotificationsBackend_Push(mention), cancellationToken).ConfigureAwait(false);
                ReAlertCounts[id] = ++count;
            }
            if (count < Constants.Notification.MaxMentionReAlerts)
                hasMore = true;
        }
        if (!hasMore)
            return Moment.MaxValue; // every unread mention exhausted its re-alerts

        return now + Constants.Notification.MentionReAlertInterval;
    }
}
