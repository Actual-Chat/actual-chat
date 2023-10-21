using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework.Operations;
using Stl.Multitenancy;

namespace ActualChat.Db;

public class DbOperationLog<TDbContext>(IServiceProvider services) : DbOperationLog<TDbContext, DbOperation>(services)
    where TDbContext : DbContext
{
    public override async Task<int> Trim(Tenant tenant, DateTime minCommitTime, int maxCount, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext(tenant, true);
        await using var _ = dbContext.ConfigureAwait(false);

        var operationsCount = await dbContext.Database
            .ExecuteSqlInterpolatedAsync($"""
                   DELETE FROM _operations
                   WHERE id = any(array(
                       SELECT id
                       FROM _operations
                       WHERE commit_time < {minCommitTime}
                       ORDER BY commit_time
                       LIMIT {maxCount}));
                """,
                cancellationToken)
            .ConfigureAwait(false);

        return operationsCount;
    }
}

