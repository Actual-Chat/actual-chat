using ActualChat.Db;
using ActualChat.Hashing;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using ActualLab.Redis;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Passkeys;

public class PasskeysBackend(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IPasskeysBackend
{
    private RedisDb<UsersDbContext> RedisDb { get; } = services.GetRequiredService<RedisDb<UsersDbContext>>();
    private UsersSettings Settings { get; } = services.GetRequiredService<UsersSettings>();
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

    public virtual async Task StoreChallenge(
        UserId userId, string prefix, string value, CancellationToken cancellationToken)
    {
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        await db.StringSetAsync(prefix + Hash(userId.Value), value, Settings.PasskeyChallengeLifetime)
            .ConfigureAwait(false);
    }

    public virtual async Task<string> ConsumeChallenge(
        UserId userId, string prefix, CancellationToken cancellationToken)
    {
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var value = await db.StringGetDeleteAsync(prefix + Hash(userId.Value)).ConfigureAwait(false);
        if (value.IsNullOrEmpty)
            throw StandardError.Constraint("This passkey request has expired. Please try again.");

        return (string)value!;
    }

    // [CommandHandler]
    public virtual async Task<PasskeyCredential?> OnChange(
        PasskeysBackend_Change command,
        CancellationToken cancellationToken)
    {
        var (userId, id, change) = command;
        var identity = UserIdentityExt.NewPasskeyIdentity(id);
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

        if (!change.IsUpdate(out _)) {
            // An identity belongs to the account, so changing the set has to move the account's version:
            // an update built on a model that still lists this identity must lose to RequireVersion rather
            // than write it back.
            var dbAccount = await dbContext.Accounts
                .FirstOrDefaultAsync(x => x.Id == userId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (dbAccount is not null)
                dbAccount.Version = VersionGenerator.NextVersion(dbAccount.Version);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Invalidation.Defer(() => {
            _ = Get(userId, id, default);
            _ = List(userId, default);
            if (change.Kind != ChangeKind.Update) {
                _ = AccountsBackend.Get(userId, default);
                _ = AccountsBackend.GetIdByUserIdentity(identity, default);
            }
        });
        return change.IsRemove() ? null : dbPasskey.ToModel();
    }

    private static string Hash(string value)
        => value.Hash().SHA256().ToBase64HashString(HashAlgorithm.SHA256);
}
