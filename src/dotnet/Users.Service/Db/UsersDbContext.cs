using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.Authentication.Services;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Users.Db;

public class UsersDbContext(DbContextOptions<UsersDbContext> options) : DbContextBase(options)
{
    public DbSet<DbKvasEntry> KvasEntries { get; protected set; } = null!;
    public DbSet<DbAccount> Accounts { get; protected set; } = null!;
    public DbSet<DbAvatar> Avatars { get; protected set; } = null!;
    public DbSet<DbUserPresence> UserPresences { get; protected set; } = null!;
    public DbSet<DbChatPosition> ChatPositions { get; protected set; } = null!;

    // ActualLab.Fusion.EntityFramework tables
    public DbSet<DbUser> Users { get; protected set; } = null!;
    public DbSet<DbUserIdentity<string>> UserIdentities { get; protected set; } = null!;
    public DbSet<DbSessionInfo> Sessions { get; protected set; } = null!;
    public DbSet<DbOperation> Operations { get; protected set; } = null!;

    protected override void OnModelCreating(ModelBuilder model)
        => model.ApplyConfigurationsFromAssembly(typeof(UsersDbContext).Assembly).UseSnakeCaseNaming();
}
