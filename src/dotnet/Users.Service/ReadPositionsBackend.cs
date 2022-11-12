using System.Diagnostics.CodeAnalysis;
using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

[SuppressMessage("Usage", "MA0006:Use String.Equals instead of equality operator")]
public class ReadPositionsBackend: DbServiceBase<UsersDbContext>, IReadPositionsBackend
{
    private IDbEntityResolver<string, DbReadPosition> DbReadPositionResolver { get; }

    public ReadPositionsBackend(IServiceProvider services) : base(services)
        => DbReadPositionResolver = services.GetRequiredService<IDbEntityResolver<string, DbReadPosition>>();

    // [ComputeMethod]
    public virtual async Task<long?> Get(string userId, string chatId, CancellationToken cancellationToken)
    {
        var id = DbReadPosition.ComposeId(userId, chatId);
        var dbReadPosition = await DbReadPositionResolver.Get(id, cancellationToken).ConfigureAwait(false);
        return dbReadPosition?.ReadEntryId;
    }

    // [CommandHandler]
    public virtual async Task Set(IReadPositionsBackend.SetCommand command, CancellationToken cancellationToken)
    {
        var (userId, chatId, readEntryId, force) = command;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            if (context.Operation().Items.GetOrDefault<bool>())
                _ = Get(userId, chatId, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var id = DbReadPosition.ComposeId(userId, chatId);
        var dbReadPosition = await dbContext.ReadPositions
            .Get(id, cancellationToken)
            .ConfigureAwait(false);
        bool hasChanges = false;
        if (dbReadPosition == null) {
            dbReadPosition = new DbReadPosition {
                Id = id,
                ReadEntryId = readEntryId,
            };
            dbContext.Add(dbReadPosition);
            hasChanges = true;
        }
        else if (readEntryId > dbReadPosition.ReadEntryId || force) {
            dbReadPosition.ReadEntryId = readEntryId;
            hasChanges = true;
        }

        if (hasChanges)
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation().Items.Set(hasChanges);
    }
}
