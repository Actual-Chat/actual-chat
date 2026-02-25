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
            ? dbContext.Accounts.ForNoKeyUpdate()
            : dbContext.Accounts;
        var dbAccount = await dbAccounts
            .FirstOrDefaultAsync(a => Equals(a.Id, userId.Value), cancellationToken)
            .ConfigureAwait(false);
        if (dbAccount is not null)
            await dbContext.Entry(dbAccount).Collection(nameof(DbAccount.Identities))
                .LoadAsync(cancellationToken).ConfigureAwait(false);
        return dbAccount;
    }

    // Returns UserId by identity from DbAccountIdentity
    public static async Task<UserId?> GetUserIdByIdentity(
        this UsersDbContext dbContext, UserIdentity userIdentity, bool forUpdate,
        CancellationToken cancellationToken)
    {
        if (!userIdentity.IsValid)
            return null;

        var id = userIdentity.Id;

        var dbAccountIdentities = forUpdate
            ? dbContext.AccountIdentities.ForNoKeyUpdate()
            : dbContext.AccountIdentities;
        var dbAccountIdentity = await dbAccountIdentities
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return dbAccountIdentity is not null
            ? UserId.ParseNullable(dbAccountIdentity.DbAccountId)
            : null;
    }
}
