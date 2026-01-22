using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users.Db;

public static class UsersDbContextExt
{
    public static async Task<DbUser?> GetDbUser(
        this UsersDbContext dbContext, string userId, bool forUpdate,
        CancellationToken cancellationToken)
    {
        var dbUsers = forUpdate
            ? dbContext.Set<DbUser>().ForNoKeyUpdate()
            : dbContext.Set<DbUser>();
        var dbUser = await dbUsers
            .FirstOrDefaultAsync(u => Equals(u.Id, userId), cancellationToken)
            .ConfigureAwait(false);
        if (dbUser is not null)
            await dbContext.Entry(dbUser).Collection(nameof(DbUser.Identities))
                .LoadAsync(cancellationToken).ConfigureAwait(false);
        return dbUser;
    }

    public static async Task<DbUser?> GetDbUserByUserIdentity(
        this UsersDbContext dbContext, UserIdentity userIdentity, bool forUpdate,
        CancellationToken cancellationToken)
    {
        if (!userIdentity.IsValid)
            return null;

        var dbUserIdentities = forUpdate
            ? dbContext.Set<DbUserIdentity<string>>().ForNoKeyUpdate()
            : dbContext.Set<DbUserIdentity<string>>();
        var id = userIdentity.Id;
        var dbUserIdentity = await dbUserIdentities
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (dbUserIdentity is null)
            return null;

        var user = await dbContext.GetDbUser(dbUserIdentity.DbUserId, forUpdate, cancellationToken).ConfigureAwait(false);
        return user;
    }
}
