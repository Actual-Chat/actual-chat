using ActualChat.Flows;
using ActualChat.Notifications.Module;

namespace ActualChat.Notifications.Flows;

/// <summary>
/// Retries <see cref="NotificationsBackend_Converge"/> until the user is owed no more dismissals,
/// so every notification that reached the blob also reaches a dismissal. One flow per user, keyed
/// by user id; resumed by the read filter, re-runs every ConvergePeriod while notifications remain.
/// </summary>
[Flow(DelayQuanta = 3)]
[DataContract, MessagePackObject(true)]
public partial class NotificationConvergeFlow : PeriodicFlow
{
    private static readonly TimeSpan RetryPeriod = TimeSpan.FromMinutes(1);

    protected override async ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken)
    {
        var userId = UserId.Parse(Id.Arguments);
        var accountsBackend = Services.GetRequiredService<IAccountsBackend>();
        var account = await accountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account?.IsGuestOrNull() != false || account.IsBot)
            return "No account";

        return FlowReadiness.Ready;
    }

    protected override async ValueTask<Moment> Run(CancellationToken cancellationToken)
    {
        // Converge commits whatever the filter is hiding and sends the dismissals those removals
        // owe. Repeated resumes collapse onto this one flow, so a hot read path costs one pass per
        // DelayQuanta, not one per read.
        var userId = UserId.Parse(Id.Arguments);
        var commander = Services.Commander();
        var notificationsBackend = Services.GetRequiredService<INotificationsBackend>();
        await commander.Call(new NotificationsBackend_Converge(userId), cancellationToken).ConfigureAwait(false);

        // Anything still owed means the send failed - come back for it rather than parking, or the
        // banner outlives the notification on every device that missed the push.
        var info = await notificationsBackend.GetUserNotificationInfo(userId, cancellationToken).ConfigureAwait(false);
        if (!info.PendingDismissals.IsEmpty)
            return Hub.SystemNow + RetryPeriod;

        // The filter only runs when something reads it, so a device that never does keeps whatever
        // went stale without a trigger - e.g. a removal whose cleanup was lost.
        var notificationsSettings = Services.GetRequiredService<NotificationsSettings>();
        return info.Items.Count == 0
            ? Moment.MaxValue
            : Hub.SystemNow + notificationsSettings.ConvergePeriod;
    }
}
