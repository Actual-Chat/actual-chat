using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.Authentication.Services;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Users.Db;

public class UsersDbContext : DbContextBase
{
    public DbSet<DbKvasEntry> KvasEntries { get; protected set; } = null!;
    public DbSet<DbAccount> Accounts { get; protected set; } = null!;
    public DbSet<DbAvatar> Avatars { get; protected set; } = null!;
    public DbSet<DbUserPresence> UserPresences { get; protected set; } = null!;
    public DbSet<DbChatPosition> ChatPositions { get; protected set; } = null!;

    // Stl.Fusion.EntityFramework tables
    public DbSet<DbUser> Users { get; protected set; } = null!;
    public DbSet<DbUserIdentity<string>> UserIdentities { get; protected set; } = null!;
    public DbSet<DbSessionInfo> Sessions { get; protected set; } = null!;
    public DbSet<DbOperation> Operations { get; protected set; } = null!;

    public UsersDbContext(DbContextOptions<UsersDbContext> options) : base(options) { }

#pragma warning disable IL2026
    protected override void OnModelCreating(ModelBuilder model)
        => model.ApplyConfigurationsFromAssembly(typeof(UsersDbContext).Assembly).UseSnakeCaseNaming();
#pragma warning restore IL2026
}
