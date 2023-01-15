using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UsersUpgradeBackend : DbServiceBase<UsersDbContext>,  IUsersUpgradeBackend
{
    public UsersUpgradeBackend(IServiceProvider services) : base(services) { }

    public async Task<ImmutableList<UserId>> ListAllUserIds(CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var userIds = await dbContext.Users
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return userIds.Select(id => new UserId(id)).ToImmutableList();
    }
}
