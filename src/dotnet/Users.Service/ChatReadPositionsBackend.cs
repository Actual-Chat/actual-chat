using System.Diagnostics.CodeAnalysis;
using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

[SuppressMessage("Usage", "MA0006:Use String.Equals instead of equality operator")]
public class ChatReadPositionsBackend: DbServiceBase<UsersDbContext>, IChatReadPositionsBackend
{
    private IDbEntityResolver<string, DbChatReadPosition> DbChatReadPositionResolver { get; }

    public ChatReadPositionsBackend(IServiceProvider services) : base(services)
        => DbChatReadPositionResolver = services.GetRequiredService<IDbEntityResolver<string, DbChatReadPosition>>();

    // [ComputeMethod]
    public virtual async Task<long?> Get(string userId, string chatId, CancellationToken cancellationToken)
    {
        var compositeId = DbChatReadPosition.ComposeId(userId, chatId);
        var position = await DbChatReadPositionResolver.Get(compositeId, cancellationToken).ConfigureAwait(false);
        return position?.ReadEntryId;
    }

    // [CommandHandler]
    public virtual async Task Set(IChatReadPositionsBackend.SetCommand command, CancellationToken cancellationToken)
    {
        var (userId, chatId, readEntryId, force) = command;
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            if (context.Operation().Items.Get<bool>())
                _ = Get(userId, chatId, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var compositeId = DbChatReadPosition.ComposeId(userId, chatId);
        var dbPosition = await dbContext.ChatReadPositions
            .Get(compositeId, cancellationToken)
            .ConfigureAwait(false);
        bool hasChanges = false;
        if (dbPosition == null) {
            dbPosition = new DbChatReadPosition {
                Id = compositeId,
                ReadEntryId = readEntryId,
            };
            dbContext.Add(dbPosition);
            hasChanges = true;
        }
        else if (readEntryId > dbPosition.ReadEntryId || force) {
            dbPosition.ReadEntryId = readEntryId;
            hasChanges = true;
        }

        if (hasChanges)
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation().Items.Set(hasChanges);
    }
}
