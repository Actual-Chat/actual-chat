using ActualChat.Db;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Passkey;

public class PasskeysBackend(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IPasskeysBackend
{
    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();

    // [ComputeMethod]
    public virtual async Task<PasskeyCredential?> Get(UserId userId, string id, CancellationToken cancellationToken)
    {
        if (id.IsNullOrEmpty())
            return null;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbPasskey = await dbContext.Passkeys
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId.Value, cancellationToken)
            .ConfigureAwait(false);
        return dbPasskey?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<PasskeyCredential>> List(UserId userId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbPasskeys = await dbContext.Passkeys
            .Where(x => x.UserId == userId.Value)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbPasskeys.Select(x => x.ToModel()).ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task<PasskeyCredential?> OnChange(
        PasskeysBackend_Change command,
        CancellationToken cancellationToken)
    {
        var (userId, id, change) = command;
        var identity = UserIdentityExt.NewPasskeyIdentity(id);
        if (Invalidation.IsActive) {
            _ = Get(userId, id, default);
            _ = List(userId, default);
            if (change.Kind != ChangeKind.Update) {
                _ = AccountsBackend.Get(userId, default);
                _ = AccountsBackend.GetIdByUserIdentity(identity, default);
            }
            return default!;
        }

        change.RequireValid();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        await dbContext.Accounts.Lock(userId, cancellationToken).ConfigureAwait(false);

        DbPasskey? dbPasskey;
        if (change.IsCreate(out var credential)) {
            if (credential.Id != id || credential.UserId != userId)
                throw StandardError.Constraint("Passkey id or owner mismatch.");

            var ownerId = await dbContext.GetUserIdByIdentity(identity, true, cancellationToken).ConfigureAwait(false);
            if (ownerId is not null)
                throw StandardError.Unauthorized("This passkey is already registered.");

            dbPasskey = new DbPasskey(credential);
            dbContext.Passkeys.Add(dbPasskey);
            dbContext.AccountIdentities.Add(new DbAccountIdentity {
                Id = identity.Id,
                DbAccountId = userId.Value,
                Secret = "",
            });
        }
        else if (change.IsUpdate(out credential)) {
            dbPasskey = await dbContext.Passkeys
                .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId.Value, cancellationToken)
                .ConfigureAwait(false);
            dbPasskey = dbPasskey.Require();
            dbPasskey.UpdateFrom(credential with { Id = id, UserId = userId });
        }
        else {
            dbPasskey = await dbContext.Passkeys
                .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (dbPasskey is null)
                return null;

            dbContext.Passkeys.Remove(dbPasskey);
            var dbIdentity = await dbContext.AccountIdentities
                .FirstOrDefaultAsync(x => x.Id == identity.Id, cancellationToken)
                .ConfigureAwait(false);
            if (dbIdentity is not null)
                dbContext.AccountIdentities.Remove(dbIdentity);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return change.IsRemove() ? null : dbPasskey.ToModel();
    }
}
