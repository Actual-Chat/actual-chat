using System.Diagnostics.CodeAnalysis;
using ActualChat.Chat;
using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

[SuppressMessage("Usage", "MA0006:Use String.Equals instead of equality operator")]
public class ChatPositionsBackend(IServiceProvider services) : DbServiceBase<UsersDbContext>(services),
    IChatPositionsBackend
{
    private IDbEntityResolver<string, DbChatPosition> DbChatPositionResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbChatPosition>>();

    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();

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
            if (context.Operation.Items.GetOrDefault<bool>())
                _ = Get(userId, chatId, kind, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var id = DbChatPosition.ComposeId(userId, chatId, kind);
        var dbChatPosition = await dbContext.ChatPositions.ForUpdate()
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
        else if (force || kind != ChatPositionKind.Read || position.EntryLid > dbChatPosition.EntryLid) {
            dbChatPosition.UpdateFrom(position);
            hasChanges = true;
        }

        if (hasChanges)
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.Set(hasChanges);

        if (kind == ChatPositionKind.Read && hasChanges) {
            var stat = await ChatsBackend.GetReadPositionsStat(chatId, cancellationToken).ConfigureAwait(false);
            var needUpdateStat = stat is null || MightUpdateStat(stat, userId, position.EntryLid);
            // Do not send update stat command if we know a priori it won't affect stat.
            if (needUpdateStat)
                await Commander.Call(new ChatsBackend_UpdateReadPositionsStat(chatId, userId, position.EntryLid),
                        true,
                        cancellationToken)
                    .ConfigureAwait(false);
        }

        static bool MightUpdateStat(ReadPositionsStatBackend stat, UserId userId, long entryLid)
        {
            if (stat.StartTrackingEntryLid > entryLid)
                return false;

            var positions = stat.TopReadPositions;
            return positions.Count == 0
                || positions.Count == 1 && (positions[0].UserId != userId || positions[0].EntryLid < entryLid)
                || positions[^1].EntryLid < entryLid;
        }
    }
}
