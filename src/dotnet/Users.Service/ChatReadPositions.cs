using System.Diagnostics.CodeAnalysis;
using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

[SuppressMessage("Usage", "MA0006:Use String.Equals instead of equality operator")]
public class ChatReadPositions: DbServiceBase<UsersDbContext>, IChatReadPositions
{
    private IAuth Auth { get; }
    private IDbEntityResolver<string, DbChatReadPosition> DbChatReadPositionResolver { get; }

    public ChatReadPositions(IServiceProvider services) : base(services)
    {
        Auth = services.GetRequiredService<IAuth>();
        DbChatReadPositionResolver = services.GetRequiredService<IDbEntityResolver<string, DbChatReadPosition>>();
    }

    // [ComputeMethod]
    public virtual async Task<long?> GetReadPosition(Session session, string chatId, CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user == null)
            return null;

        var compositeId = DbChatReadPosition.ComposeId(user.Id, chatId);
        var position = await DbChatReadPositionResolver.Get(compositeId, cancellationToken).ConfigureAwait(false);
        return position?.ReadEntryId;
    }

    // [CommandHandler]
    public virtual async Task UpdateReadPosition(IChatReadPositions.UpdateReadPositionCommand command, CancellationToken cancellationToken)
    {
        var (session, chatId, entryId) = command;
        if (Computed.IsInvalidating()) {
            _ = GetReadPosition(session, chatId, default);
            return;
        }

        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user == null)
            return;

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbPosition = await dbContext.ChatReadPositions
            .FindAsync(DbKey.Compose(DbChatReadPosition.ComposeId(user.Id, chatId)), cancellationToken)
            .ConfigureAwait(false);
        if (dbPosition == null) {
            dbPosition = new DbChatReadPosition {
                Id = DbChatReadPosition.ComposeId(user.Id, chatId),
                ReadEntryId = entryId,
            };
            dbContext.Add(dbPosition);
        }
        else {
            if (entryId > dbPosition.ReadEntryId)
                dbPosition.ReadEntryId = entryId;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
