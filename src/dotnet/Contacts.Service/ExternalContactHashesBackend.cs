using ActualChat.Contacts.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Contacts;

public class ExternalContactHashesBackend(IServiceProvider services) : DbServiceBase<ContactsDbContext>(services), IExternalContactHashesBackend
{
    private IDbEntityResolver<string, DbExternalContactsHash> DbDbExternalContactsHashResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbExternalContactsHash>>();

    // [ComputeMethod]
    public virtual async Task<ExternalContactsHash?> Get(UserDeviceId userDeviceId, CancellationToken cancellationToken)
    {
        var dbExternalContactsHash = await DbDbExternalContactsHashResolver.Get(userDeviceId.Value, cancellationToken).ConfigureAwait(false);
        var externalContactsHash = dbExternalContactsHash?.ToModel();
        // Keep logging for debugging VersionMismatchException
        Log.LogInformation("<- Get: userDeviceId={UserDeviceId}, externalContactsHash={ExternalContactsHash}", userDeviceId, externalContactsHash);
        return externalContactsHash;
    }

    // [CommandHandler]
    public virtual async Task<ExternalContactsHash?> OnChange(
        ExternalContactHashesBackend_Change command,
        CancellationToken cancellationToken)
    {
        var (userDeviceId, expectedVersion, change) = command;
        if (Invalidation.IsActive) {
            _ = Get(userDeviceId, default);
            return null!;
        }

        // Keep logging for debugging VersionMismatchException
        Log.LogInformation("-> OnChange: userDeviceId={UserDeviceId}, expectedVersion={ExpectedVersion}, change={Change}",
            userDeviceId, expectedVersion, change);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbHash = await dbContext.ExternalContactsHashes.ForUpdate()
            .FirstOrDefaultAsync(c => c.Id == userDeviceId.Value, cancellationToken)
            .ConfigureAwait(false);
        var existing = dbHash?.ToModel();
        var now = Clocks.SystemClock.Now;
        // Keep logging for debugging VersionMismatchException
        Log.LogInformation("-> OnChange: existing={Existing}", existing);

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
    public virtual async Task OnRemoveAccount(
        ExternalContactHashesBackend_RemoveAccount command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        if (Invalidation.IsActive)
            return; // we can skip invalidation for own contacts

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var idPrefix = UserDeviceId.Format(userId, "");
        // we remove contacts without invalidation since nobody else sees these contacts
        await dbContext.ExternalContactsHashes
            .Where(a => a.Id.StartsWith(idPrefix)) // This is faster than index-based approach
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
