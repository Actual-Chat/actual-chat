using ActualChat.Contacts.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Contacts;

public class ExternalContactHashesBackend(
    IDbEntityResolver<string, DbExternalContactsHash> externalContactsHashResolver,
    IServiceProvider services) : DbServiceBase<ContactsDbContext>(services), IExternalContactHashesBackend
{
    // [ComputeMethod]
    public virtual async Task<ExternalContactsHash?> Get(UserDeviceId userDeviceId, CancellationToken cancellationToken)
    {
        userDeviceId.Require();

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbExternalContactsHash = await externalContactsHashResolver.Get(userDeviceId.Value, cancellationToken).ConfigureAwait(false);
        return dbExternalContactsHash?.ToModel();
    }

    // [CommandHandler]
    public virtual async Task<ExternalContactsHash?> OnChange(
        ExternalContactHashesBackend_Change command,
        CancellationToken cancellationToken)
    {
        var (userDeviceId, expectedVersion, change) = command;
        if (Computed.IsInvalidating()) {
            _ = Get(userDeviceId, default);
            return default!;
        }

        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbHash = await dbContext.ExternalContactsHashes.ForUpdate()
            .FirstOrDefaultAsync(c => c.Id == userDeviceId.Value, cancellationToken)
            .ConfigureAwait(false);
        var existing = dbHash?.ToModel();
        var now = Clocks.SystemClock.Now;

        if (change.IsCreate(out var hash)) {
            if (existing != null)
                return existing; // Already exists, so we don't recreate one

            hash = hash with {
                Id = userDeviceId,
                Version = VersionGenerator.NextVersion(),
                CreatedAt = now,
                ModifiedAt = now,
            };
            dbHash = new DbExternalContactsHash(hash);
            dbContext.Add(dbHash);
        }
        else if (change.IsUpdate(out hash)) {
            dbHash.RequireVersion(expectedVersion);
            hash = hash with {
                Version = VersionGenerator.NextVersion(dbHash.Version),
                ModifiedAt = now,
            };
            dbHash.UpdateFrom(hash);
            dbContext.ExternalContactsHashes.Update(dbHash);
        }
        else {
            // Remove
            if (dbHash == null)
                return null;
            dbHash.RequireVersion(expectedVersion);
            dbContext.Remove(dbHash);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbHash.ToModel();
    }

    // [CommandHandler]
    public virtual async Task OnRemoveAccount(ExternalContactHashesBackend_RemoveAccount command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        if (Computed.IsInvalidating())
            return; // we can skip invalidation for own contacts

        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var idPrefix = UserDeviceId.Prefix(userId);
        // we remove contacts without invalidation since nobody else sees these contacts
        await dbContext.ExternalContactsHashes
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
