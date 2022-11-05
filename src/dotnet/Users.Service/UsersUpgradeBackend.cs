using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

[Obsolete]
public class UsersUpgradeBackend : DbServiceBase<UsersDbContext>,  IUsersUpgradeBackend
{
    public UsersUpgradeBackend(IServiceProvider services) : base(services)
    { }

    public virtual async Task<ImmutableList<string>> ListAllUserIds(CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var userIds = await dbContext.Users
            .Select(c => c.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return userIds.ToImmutableList();
    }
}
