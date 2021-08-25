using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Db
{
    public abstract class DbInitializer<TDbContext> : DbServiceBase<TDbContext>, IDbInitializer
        where TDbContext : DbContext
    {
        public bool ShouldRecreateDb { get; set; }
        public Dictionary<IDbInitializer, Task> InitializeTasks { get; set; } = null!;

        protected DbInitializer(IServiceProvider services) : base(services) { }

        public virtual async Task Initialize(CancellationToken cancellationToken)
        {
            await using var dbContext = DbContextFactory.CreateDbContext();
            var db = dbContext.Database;
            if (ShouldRecreateDb)
                await db.EnsureDeletedAsync(cancellationToken);
            await db.EnsureCreatedAsync(cancellationToken);
        }
    }
}
