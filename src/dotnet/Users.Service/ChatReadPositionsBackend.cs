using System.Diagnostics.CodeAnalysis;
using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

[SuppressMessage("Usage", "MA0006:Use String.Equals instead of equality operator")]
public class ChatReadPositionsBackend: DbServiceBase<UsersDbContext>, IChatReadPositionsBackend
{
    private IAccountsBackend AccountsBackend { get; }
    private IDbEntityResolver<string, DbChatReadPosition> DbChatReadPositionResolver { get; }

    public ChatReadPositionsBackend(IServiceProvider services) : base(services)
    {
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        DbChatReadPositionResolver = services.GetRequiredService<IDbEntityResolver<string, DbChatReadPosition>>();
    }

    // [ComputeMethod]
    public virtual async Task<long?> Get(string userId, string chatId, CancellationToken cancellationToken)
    {
        var compositeId = DbChatReadPosition.ComposeId(userId, chatId);
        var position = await DbChatReadPositionResolver.Get(compositeId, cancellationToken).ConfigureAwait(false);
        return position?.ReadEntryId;
    }

    // [CommandHandler]
    public virtual async Task Set(IChatReadPositionsBackend.SetReadPositionCommand command, CancellationToken cancellationToken)
    {
        var (userId, chatId, readEntryId) = command;
        if (Computed.IsInvalidating()) {
            _ = Get(userId, chatId, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var compositeId = DbChatReadPosition.ComposeId(userId, chatId);
        var dbPosition = await dbContext.ChatReadPositions
            .FindAsync(DbKey.Compose(compositeId), cancellationToken)
            .ConfigureAwait(false);
        if (dbPosition == null) {
            dbPosition = new DbChatReadPosition {
                Id = compositeId,
                ReadEntryId = readEntryId,
            };
            dbContext.Add(dbPosition);
        }
        else {
            if (readEntryId > dbPosition.ReadEntryId)
                dbPosition.ReadEntryId = readEntryId;
        }
        // Log.LogInformation("Read position update: user #{UserId} chat #{ChatId} -> {ReadEntryId}",
        //     userId, chatId, readEntryId);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
