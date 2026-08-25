using ActualChat.Flows;

namespace ActualChat.Notifications.Flows;

/// <summary>
/// Commits the removals that <see cref="INotificationsBackend.GetUserNotificationInfo"/>'s filter
/// only hides, so every notification that reached the blob also reaches a dismissal. One flow per
/// user, keyed by user id; resumed by that filter and parked as soon as it has run.
/// </summary>
[Flow(DelayQuanta = 3)]
[DataContract, MessagePackObject(true)]
public partial class NotificationCleanupFlow : PeriodicFlow
{
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
        // One pass is enough: the filter re-triggers this flow if anything is still hidden, and
        // DelayQuanta bounds how often that can turn into work. Repeated resumes collapse onto
        // this one flow, so a hot read path costs one cleanup per quantum, not one per read.
        var userId = UserId.Parse(Id.Arguments);
        var commander = Services.Commander();
        await commander.Call(new NotificationsBackend_Cleanup(userId), cancellationToken).ConfigureAwait(false);
        return Moment.MaxValue;
    }
}
