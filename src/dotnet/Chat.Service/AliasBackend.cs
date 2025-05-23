using ActualChat.Chat.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public class AliasBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IAliasBackend
{
    private IDbEntityResolver<string, DbAlias> DbAliasResolver { get; } = services.GetRequiredService<IDbEntityResolver<string, DbAlias>>();

    public virtual async Task<Alias?> Get(AliasId aliasId, CancellationToken cancellationToken)
    {
        var dbAlias = await DbAliasResolver.Get(aliasId.NormalizedValue, cancellationToken).ConfigureAwait(false);
        return dbAlias?.ToModel();
    }

    public virtual async Task<Alias?> OnChange(AliasBackend_Change command, CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = command;

        if (Invalidation.IsActive) {
            _ = Get(id, default);
            return default!;
        }

        id.Require();
        change.RequireValid();
        var sid = id.NormalizedValue;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbAlias = await dbContext.Aliases.ForUpdate()
            .FirstOrDefaultAsync(c => c.Id == sid, cancellationToken)
            .ConfigureAwait(false);

        if (change.IsCreate(out var alias)) {
            if (dbAlias != null)
                throw StandardError.Constraint($"Alias with id '{sid}' already exists.");

            // Checks

            if (!alias.Id.Equals(id))
                throw StandardError.Constraint("Alias.Id should match command.AliasId.");

            if (alias.TargetId.IsNullOrEmpty())
                throw StandardError.Constraint("Alias.TargetId should not be empty.");

            alias = alias with {
                Id = id,
                Version = VersionGenerator.NextVersion(),
                CreatedAt = Clocks.SystemClock.Now,
            };
            dbAlias = new DbAlias(alias);
            dbContext.Add(dbAlias);
        }
        else if (change.IsUpdate(out _))
            throw StandardError.NotSupported("Update change is not allowed.");
        else { // Remove
            if (expectedVersion != null)
                dbAlias.RequireVersion(expectedVersion);
            if (dbAlias == null)
                return null;

            dbContext.Remove(dbAlias);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        alias = dbAlias.ToModel();
        return alias;
    }
}
