using System;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.DependencyInjection;
using Stl.Fusion.EntityFramework.Extensions;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Todos
{
    public class TodosDbContext : DbContext
    {
        // Stl.Fusion.EntityFramework tables
        public DbSet<DbKeyValue> KeyValues { get; protected set; } = null!;
        public DbSet<DbOperation> Operations { get; protected set; } = null!;

        public TodosDbContext(DbContextOptions options) : base(options) { }
    }

    [RegisterService(typeof(IDbInitializer), IsEnumerable = true)]
    public class TodosDbInitializer : DbInitializer<TodosDbContext>
    {
        public TodosDbInitializer(IServiceProvider services) : base(services) { }
    }
}
