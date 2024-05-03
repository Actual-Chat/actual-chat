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

    // ActualLab.Fusion.Authentication.Services tables
    public DbSet<DbUser> Users { get; protected set; } = null!;
    public DbSet<DbUserIdentity<string>> UserIdentities { get; protected set; } = null!;
    public DbSet<DbSessionInfo> Sessions { get; protected set; } = null!;

    // ActualLab.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;
    public DbSet<DbEvent> Events { get; protected set; } = null!;

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ApplyConfigurationsFromAssembly(typeof(UsersDbContext).Assembly).UseSnakeCaseNaming();

        var kvasEntry = model.Entity<DbKvasEntry>();
        kvasEntry.Property(e => e.Key).UseCollation("C");

        var account = model.Entity<DbAccount>();
        account.Property(e => e.Id).UseCollation("C");

        var avatar = model.Entity<DbAvatar>();
        avatar.Property(e => e.Id).UseCollation("C");
        avatar.Property(e => e.UserId).UseCollation("C");
        avatar.Property(e => e.AvatarKey).UseCollation("C");

        var userPresence = model.Entity<DbUserPresence>();
        userPresence.Property(e => e.UserId).UseCollation("C");

        var chatPosition = model.Entity<DbChatPosition>();
        chatPosition.Property(e => e.Id).UseCollation("C");

        var user = model.Entity<DbUser>();
        user.Property(e => e.Id).UseCollation("C");

        var userIdentity = model.Entity<DbUserIdentity<string>>();
        userIdentity.Property(e => e.Id).UseCollation("C");
        userIdentity.Property(e => e.DbUserId).UseCollation("C");

        var sessionInfo = model.Entity<DbSessionInfo>();
        sessionInfo.Property(e => e.Id).UseCollation("C");
        sessionInfo.Property(e => e.UserId).UseCollation("C");

        var operation = model.Entity<DbOperation>();
        operation.Property(e => e.Uuid).UseCollation("C");
        operation.Property(e => e.HostId).UseCollation("C");

        var events = model.Entity<DbEvent>();
        events.Property(e => e.Uuid).UseCollation("C");
    }
}
