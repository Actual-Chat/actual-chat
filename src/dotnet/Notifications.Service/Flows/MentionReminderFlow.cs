using ActualChat.Flows;
using ActualChat.Queues;
using ActualChat.Users;

namespace ActualChat.Notifications.Flows;

/// <summary>
/// Re-alerts a user about their unread @mentions on a fixed interval until they're read. One flow
/// per user, keyed by user id; started when a mention is delivered, stops once none remain unread.
/// </summary>
[Flow(DelayQuanta = 60)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class MentionReminderFlow : PeriodicFlow
{
    public static bool IsDue(Notification mention, Moment now)
        => now - mention.SentAt >= Constants.Notification.MentionReAlertInterval;

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
        if (mentions.Count == 0)
            return Moment.MaxValue; // nothing unread -> stop until a new mention restarts the flow

        var now = Hub.SystemNow;
        var queues = Services.Queues();
        foreach (var mention in mentions)
            if (IsDue(mention, now))
                await queues.Enqueue(new NotificationsBackend_Push(mention), cancellationToken).ConfigureAwait(false);

        return now + Constants.Notification.MentionReAlertInterval;
    }
}
