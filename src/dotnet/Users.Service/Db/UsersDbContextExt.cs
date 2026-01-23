using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users.Db;

public static class UsersDbContextExt
{
    public static async Task<DbAccount?> GetDbAccount(
        this UsersDbContext dbContext, UserId userId, bool forUpdate,
        CancellationToken cancellationToken)
    {
        var dbAccounts = forUpdate
            ? dbContext.Set<DbAccount>().ForNoKeyUpdate()
            : dbContext.Set<DbAccount>();
        var dbAccount = await dbAccounts
            .FirstOrDefaultAsync(a => Equals(a.Id, userId.Value), cancellationToken)
            .ConfigureAwait(false);
        if (dbAccount is not null)
            await dbContext.Entry(dbAccount).Collection(nameof(DbAccount.Identities))
                .LoadAsync(cancellationToken).ConfigureAwait(false);
        return dbAccount;
    }

    public static async Task<DbUser?> GetDbUser(
        this UsersDbContext dbContext, UserId userId, bool forUpdate,
        CancellationToken cancellationToken)
    {
        var dbUsers = forUpdate
            ? dbContext.Set<DbUser>().ForNoKeyUpdate()
            : dbContext.Set<DbUser>();
        var dbUser = await dbUsers
            .FirstOrDefaultAsync(u => Equals(u.Id, userId.Value), cancellationToken)
            .ConfigureAwait(false);
        if (dbUser is not null)
            await dbContext.Entry(dbUser).Collection(nameof(DbUser.Identities))
                .LoadAsync(cancellationToken).ConfigureAwait(false);
        return dbUser;
    }

    // Returns UserId by identity - tries DbAccountIdentity first, falls back to DbUserIdentity
    public static async Task<UserId?> GetUserIdByIdentity(
        this UsersDbContext dbContext, UserIdentity userIdentity, bool forUpdate,
        CancellationToken cancellationToken)
    {
        if (!userIdentity.IsValid)
            return null;

        var id = userIdentity.Id;

        // Try DbAccountIdentity first (for FormatVersion >= 1 accounts)
        var dbAccountIdentities = forUpdate
            ? dbContext.Set<DbAccountIdentity>().ForNoKeyUpdate()
            : dbContext.Set<DbAccountIdentity>();
        var dbAccountIdentity = await dbAccountIdentities
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (dbAccountIdentity is not null)
            return UserId.ParseNullable(dbAccountIdentity.DbAccountId);

        // Fall back to DbUserIdentity (for FormatVersion == 0 accounts)
        var dbUserIdentities = forUpdate
            ? dbContext.Set<DbUserIdentity<string>>().ForNoKeyUpdate()
            : dbContext.Set<DbUserIdentity<string>>();
        var dbUserIdentity = await dbUserIdentities
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return UserId.ParseNullable(dbUserIdentity?.DbUserId);
    }
}
