using ActualChat.Flows;
using ActualChat.Notifications.Module;

namespace ActualChat.Notifications.Flows;

/// <summary>
/// Resumes <see cref="NotificationConvergeFlow"/> for every member of a chat whose entries were
/// removed, RemovalCleanupBatchSize members per RemovalCleanupBatchPeriod. A removal during a sweep
/// earns one more sweep once it ends, since the members it passed may have converged before it.
/// </summary>
[Flow(DelayQuanta = 3)]
[DataContract, MessagePackObject(true)]
public sealed partial class ChatNotificationConvergeFlow : BatchedSweepFlow
{
    private NotificationsSettings Settings => field ??= Services.GetRequiredService<NotificationsSettings>();

    private INotificationsBackend NotificationsBackend
        => field ??= Services.GetRequiredService<INotificationsBackend>();

    [DataMember(Order = 10), Key(10)]
    public string LastUserId { get; set; } = "";

    // Protected/internal methods

    protected override ValueTask StartSweep(CancellationToken cancellationToken)
    {
        LastUserId = "";
        return default;
    }

    protected override async ValueTask<Moment?> RunBatch(CancellationToken cancellationToken)
    {
        // Ringer importance bypasses the notification mode: a member who muted the chat after a
        // notification arrived still holds it.
        var chatId = ChatId.Parse(Id.Arguments);
        var userIds = await NotificationsBackend
            .ListSubscribedUserIds(chatId, NotificationImportance.Ringer, cancellationToken)
            .ConfigureAwait(false);
        var batchSize = Math.Max(1, Settings.RemovalCleanupBatchSize);
        var batch = userIds
            .Select(x => x.Value)
            .Where(x => string.CompareOrdinal(x, LastUserId) > 0)
            .Order()
            .Take(batchSize)
            .ToList();
        foreach (var userId in batch)
            Runtime.StagedEvents.Add(Hub.NewResumeEvent<NotificationConvergeFlow>(userId));
        if (batch.Count < batchSize)
            return null;

        LastUserId = batch[^1];
        return Hub.SystemNow + Settings.RemovalCleanupBatchPeriod;
    }
}
