using System.Diagnostics.CodeAnalysis;
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

        if (InvalidationMode.IsOn) {
            if (context.Operation.Items.GetOrDefault<bool>())
                _ = Get(userId, chatId, kind, default);
            return;
        }

        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
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
    }
}
