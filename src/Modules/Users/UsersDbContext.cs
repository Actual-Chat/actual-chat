using System;
using ActualChat.Db;
using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Stl.DependencyInjection;
using Stl.Fusion.EntityFramework.Authentication;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Users
{
    public class UsersDbContext : DbContext
    {
        // Stl.Fusion.EntityFramework tables
        public DbSet<DbUser> Users { get; protected set; } = null!;
        public DbSet<DbUserIdentity> UserIdentities { get; protected set; } = null!;
        public DbSet<DbSessionInfo> Sessions { get; protected set; } = null!;
        public DbSet<DbOperation> Operations { get; protected set; } = null!;

        public UsersDbContext(DbContextOptions options) : base(options) { }
    }

    [RegisterService(typeof(IDataInitializer), IsEnumerable = true)]
    public class UsersDbInitializer : DbInitializer<UsersDbContext>
    {
        public UsersDbInitializer(IServiceProvider services) : base(services) { }
    }
}
