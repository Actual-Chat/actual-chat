using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UsersUpgradeBackend(IServiceProvider services)
    : DbServiceBase<UsersDbContext>(services), IUsersUpgradeBackend
{
    public async Task<ImmutableList<UserId>> ListAllUserIds(CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var userIds = await dbContext.Users
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return userIds.Select(UserId.Parse).ToImmutableList();
    }
}
