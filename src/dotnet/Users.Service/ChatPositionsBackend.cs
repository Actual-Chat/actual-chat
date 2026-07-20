using ActualChat.Db;
using ActualChat.Queues;
using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

/// <summary>
/// Backend service implementation for storing and retrieving chat read positions.
/// </summary>
[SuppressMessage("Usage", "MA0006:Use String.Equals instead of equality operator")]
public class ChatPositionsBackend(IServiceProvider services) : DbServiceBase<UsersDbContext>(services),
    IChatPositionsBackend
{
    private IDbEntityResolver<string, DbChatPosition> DbChatPositionResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbChatPosition>>();

    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private IQueues Queues { get; } = services.Queues();

    // [ComputeMethod]
    public virtual async Task<ChatPosition> Get(
        UserId userId, ChatId chatId, ChatPositionKind kind,
        CancellationToken cancellationToken)
    {
        var id = DbChatPosition.ComposeId(userId, chatId, kind);
        var dbChatPosition = await DbChatPositionResolver.Get(id, cancellationToken).ConfigureAwait(false);
        return dbChatPosition?.ToModel() ?? new ChatPosition();
    }

    // [CommandHandler]
    public virtual async Task OnSet(ChatPositionsBackend_Set command, CancellationToken cancellationToken)
    {
        var (userId, chatId, kind, position, force) = command;
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            if (context.Operation.Items.KeylessGet<bool>())
                _ = Get(userId, chatId, kind, default);
            return;
        }

        // Guard against the client's "unbounded" sentinel (long.MaxValue) being persisted as a
        // read position. OnSet is forward-only for reads, so a stored MaxValue would mark the chat
        // permanently fully-read and suppress every notification. Clamp it to the chat's last entry.
        if (kind == ChatPositionKind.Read && position.EntryLid == long.MaxValue) {
            var maxLid = await ChatsBackend.GetMaxLid(chatId, true, cancellationToken).ConfigureAwait(false);
            position = position with { EntryLid = maxLid };
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var id = DbChatPosition.ComposeId(userId, chatId, kind);
        await dbContext.ChatPositions.Lock(id, cancellationToken).ConfigureAwait(false);
        var dbChatPosition = await dbContext.ChatPositions
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);
        bool hasChanges = false;
        if (dbChatPosition == null) {
            dbChatPosition = new DbChatPosition {
                Id = id,
                Kind = kind,
            };
            dbChatPosition.UpdateFrom(position);
            dbContext.Add(dbChatPosition);
            hasChanges = true;
        }
        else if (force || kind == ChatPositionKind.View || position.EntryLid > dbChatPosition.EntryLid) {
            dbChatPosition.UpdateFrom(position);
            hasChanges = true;
        }

        if (hasChanges)
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(hasChanges);

        if (hasChanges && kind is ChatPositionKind.Read or ChatPositionKind.Heard) {
            if (kind == ChatPositionKind.Read)
                context.Operation
                    .AddEvent(new ReadPositionChangedEvent(userId, chatId, position.EntryLid))
                    .SetDelayBy(TimeSpan.Zero, Constants.Notification.ReadReconcileWindow, $"ReadPosChanged:{userId.Value}:{chatId.Value}");

            var stat = await ChatsBackend.GetReadPositionsStat(chatId, cancellationToken).ConfigureAwait(false);
            var needUpdateStat = stat is null || MightUpdateStat(stat, userId, position.EntryLid);
            if (needUpdateStat)
                await Queues.Enqueue(new ChatsBackend_UpdateReadPositionsStat(chatId, userId, position.EntryLid), cancellationToken).ConfigureAwait(false);
        }
        return;

        static bool MightUpdateStat(ReadPositionsStatBackend stat, UserId userId, long entryLid)
        {
            if (stat.StartTrackingEntryLid > entryLid)
                return false;

            var positions = stat.TopReadPositions;
            return positions.Length == 0
                || (positions.Length == 1 && (positions[0].UserId != userId || positions[0].EntryLid < entryLid))
                || positions[^1].EntryLid < entryLid;
        }
    }
}
