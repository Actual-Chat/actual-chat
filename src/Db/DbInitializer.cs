using System;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Db
{
    public abstract class DbInitializer<TDbContext> : DbServiceBase<TDbContext>, IDataInitializer
        where TDbContext : DbContext
    {
        protected DbInitializer(IServiceProvider services) : base(services) { }

        public virtual async Task Initialize(bool recreate, CancellationToken cancellationToken = default)
        {
            await using var dbContext = DbContextFactory.CreateDbContext();
            var db = dbContext.Database;
            if (recreate)
                await db.EnsureDeletedAsync(cancellationToken);
            await db.EnsureCreatedAsync(cancellationToken);
        }
    }
}
