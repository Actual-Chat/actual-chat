using System.Diagnostics.CodeAnalysis;
using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

[SuppressMessage("Usage", "MA0006:Use String.Equals instead of equality operator")]
public class ChatReadPositions: DbServiceBase<UsersDbContext>, IChatReadPositions
{
    private readonly IAuth _auth;

    public ChatReadPositions(IServiceProvider services, IAuth auth) : base(services)
        => _auth = auth;

    // [ComputeMethod]
    public virtual async Task<long?> GetReadPosition(Session session, string chatId, CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (!user.IsAuthenticated)
            return null;

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        string userId = user.Id;
        var position = await dbContext.ChatReadPositions
            .FirstOrDefaultAsync(d => d.UserId == userId && d.ChatId == chatId, cancellationToken)
            .ConfigureAwait(false);
        return position?.EntryId;
    }

    // [CommandHandler]
    public virtual async Task UpdateReadPosition(IChatReadPositions.UpdateReadPositionCommand command, CancellationToken cancellationToken)
    {
        var (session, chatId, entryId) = command;
        if (Computed.IsInvalidating()) {
            _ = GetReadPosition(session, chatId, default);
            return;
        }

        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (!user.IsAuthenticated)
            return;

        string userId = user.Id;
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        var existingPosition = await dbContext.ChatReadPositions
            .FindAsync(new object?[] { userId, chatId }, cancellationToken)
            .ConfigureAwait(false);

        var dbDevice = existingPosition;
        if (dbDevice == null) {
            dbDevice = new DbChatReadPosition {
                UserId = userId,
                ChatId = chatId,
                EntryId = entryId,
            };
            dbContext.Add(dbDevice);
        }
        else
            dbDevice.EntryId = entryId;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
