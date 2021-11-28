using ActualChat.Db.Module;
using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Db;

public abstract class DbInitializer<TDbContext> : DbServiceBase<TDbContext>, IDbInitializer
    where TDbContext : DbContext
{
    protected DbInfo<TDbContext> DbInfo { get; }

    protected DbInitializer(IServiceProvider services) : base(services)
        => DbInfo = Services.GetRequiredService<DbInfo<TDbContext>>();

    public Dictionary<IDbInitializer, Task> InitializeTasks { get; set; } = null!;

    public virtual async Task Initialize(CancellationToken cancellationToken)
    {
        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var db = dbContext.Database;
        if (DbInfo.ShouldRecreateDb)
            await db.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
        await db.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        var databaseName = db.GetDbConnection().Database;
        await dbContext.Database
            .ExecuteSqlRawAsync(
                $"ALTER DATABASE \"{databaseName}\" SET DEFAULT_TRANSACTION_ISOLATION TO 'repeatable read';",
                cancellationToken)
            .ConfigureAwait(false);
    }
}
