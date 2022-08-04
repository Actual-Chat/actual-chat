using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Authentication;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Users.Db;

public class UsersDbContext : DbContextBase
{
    public DbSet<DbAccount> Accounts { get; protected set; } = null!;
    public DbSet<DbUserPresence> UserPresences { get; protected set; } = null!;
    public DbSet<DbUserAvatar> UserAvatars { get; protected set; } = null!;
    public DbSet<DbUserContact> UserContacts { get; protected set; } = null!;
    public DbSet<DbChatUserSettings> ChatUserSettings { get; protected set; } = null!;
    public DbSet<DbChatReadPosition> ChatReadPositions { get; protected set; } = null!;
    public DbSet<DbKvasEntry> KvasEntries { get; protected set; } = null!;

    // Stl.Fusion.EntityFramework tables
    public DbSet<DbUser> Users { get; protected set; } = null!;
    public DbSet<DbUserIdentity<string>> UserIdentities { get; protected set; } = null!;
    public DbSet<DbSessionInfo> Sessions { get; protected set; } = null!;
    public DbSet<DbOperation> Operations { get; protected set; } = null!;

    public UsersDbContext(DbContextOptions options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder model)
        => model.ApplyConfigurationsFromAssembly(typeof(UsersDbContext).Assembly).UseSnakeCaseNaming();
}
