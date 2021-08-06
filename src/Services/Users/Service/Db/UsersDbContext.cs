using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework.Authentication;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Users.Db
{
    public class UsersDbContext : DbContext
    {
        // Stl.Fusion.EntityFramework tables
        public DbSet<DbAppUser> Users { get; protected set; } = null!;
        public DbSet<DbUserIdentity> UserIdentities { get; protected set; } = null!;
        public DbSet<DbAppSessionInfo> Sessions { get; protected set; } = null!;
        public DbSet<DbOperation> Operations { get; protected set; } = null!;

        public UsersDbContext(DbContextOptions options) : base(options) { }
    }
}
