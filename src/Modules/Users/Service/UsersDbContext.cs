using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
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
}
