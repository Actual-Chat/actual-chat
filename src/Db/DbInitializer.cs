using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Db
{
    public interface IDbInitializer
    {
        Task InitializeDb(bool recreate, CancellationToken cancellationToken = default);
    }

    public abstract class DbInitializer<TDbContext> : DbServiceBase<TDbContext>, IDbInitializer
        where TDbContext : DbContext
    {
        protected DbInitializer(IServiceProvider services) : base(services) { }

        public virtual async Task InitializeDb(bool recreate, CancellationToken cancellationToken = default)
        {
            await using var dbContext = DbContextFactory.CreateDbContext();
            var db = dbContext.Database;
            if (recreate)
                await db.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
            await db.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
