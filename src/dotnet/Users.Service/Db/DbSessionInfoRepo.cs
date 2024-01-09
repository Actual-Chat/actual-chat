using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.Authentication.Services;
using ActualLab.Multitenancy;

namespace ActualChat.Users.Db;

public class DbSessionInfoRepo(DbAuthService<UsersDbContext>.Options settings, IServiceProvider services)
    : DbSessionInfoRepo<UsersDbContext, DbSessionInfo, string>(settings, services)
{
    public override async Task<int> Trim(
        Tenant tenant,
        DateTime maxLastSeenAt,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        var dbContext = CreateDbContext(tenant, true);
        await using var _ = dbContext.ConfigureAwait(false);

        var operationsCount = await dbContext.Database
            .ExecuteSqlInterpolatedAsync($"""
                DELETE FROM _sessions
                WHERE id = any(array(
                    SELECT id
                    FROM _sessions
                    WHERE last_seen_at < {maxLastSeenAt}
                    ORDER BY last_seen_at
                    LIMIT {maxCount}));
                """,
                cancellationToken)
            .ConfigureAwait(false);

        return operationsCount;
    }
}
