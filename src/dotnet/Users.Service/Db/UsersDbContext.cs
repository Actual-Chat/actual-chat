using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Users.Db;

public class UsersDbContext(DbContextOptions<UsersDbContext> options) : DbContextBase(options)
{
    public DbSet<DbMaintenance> Maintenances { get; protected set; } = null!;
    public DbSet<DbKvasEntry> KvasEntries { get; protected set; } = null!;
    public DbSet<DbAccount> Accounts { get; protected set; } = null!;
    public DbSet<DbAccountIdentity> AccountIdentities { get; protected set; } = null!;
    public DbSet<DbSession> Sessions { get; protected set; } = null!;
    public DbSet<DbUserSession> UserSessions { get; protected set; } = null!;
    public DbSet<DbAvatar> Avatars { get; protected set; } = null!;
    public DbSet<DbUserPresence> UserPresences { get; protected set; } = null!;
    public DbSet<DbChatPosition> ChatPositions { get; protected set; } = null!;
    public DbSet<DbPasskey> Passkeys { get; protected set; } = null!;
    public DbSet<DbUserVoice> UserVoices { get; protected set; } = null!;
    public DbSet<DbUsageEvent> UsageEvents { get; protected set; } = null!;
    public DbSet<DbUsageDay> UsageDays { get; protected set; } = null!;
    public DbSet<DbCoachEvent> CoachEvents { get; protected set; } = null!;
    public DbSet<DbCoachDay> CoachDays { get; protected set; } = null!;
    public DbSet<DbUserAttribution> UserAttributions { get; protected set; } = null!;
    public DbSet<DbUserMilestone> UserMilestones { get; protected set; } = null!;
    public DbSet<DbUserMilestoneDelivery> UserMilestoneDeliveries { get; protected set; } = null!;
    public DbSet<DbUserAnalyticsClient> UserAnalyticsClients { get; protected set; } = null!;

    // ActualLab.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;
    public DbSet<DbEvent> Events { get; protected set; } = null!;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Conventions.Add(_ => new RemoveDbEventIndexesConvention());
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        // This assembly also holds OAuthDbContext's entity configurations - an unfiltered scan
        // would pull them into the users model. Scan Users types only. See OAuthDbContext.
        model.ApplyConfigurationsFromAssembly(
                typeof(UsersDbContext).Assembly,
                t => t.Namespace?.StartsWith("ActualChat.Users", StringComparison.Ordinal) == true)
            .UseSnakeCaseNaming();

        model.Entity<DbMaintenance>().Property(e => e.Id).UseCollation("C");

        var kvasEntry = model.Entity<DbKvasEntry>();
        kvasEntry.Property(e => e.Key).UseCollation("C");

        var account = model.Entity<DbAccount>();
        account.Property(e => e.Id).UseCollation("C");

        var accountIdentity = model.Entity<DbAccountIdentity>();
        accountIdentity.Property(e => e.Id).UseCollation("C");
        accountIdentity.Property(e => e.DbAccountId).UseCollation("C");

        var sessionInfo = model.Entity<DbSession>();
        sessionInfo.Property(e => e.Id).UseCollation("C");
        sessionInfo.Property(e => e.UserId).UseCollation("C");

        var userSession = model.Entity<DbUserSession>();
        userSession.HasKey(e => new { e.UserId, e.SessionId });
        userSession.Property(e => e.UserId).UseCollation("C");
        userSession.Property(e => e.SessionId).UseCollation("C");

        var avatar = model.Entity<DbAvatar>();
        avatar.Property(e => e.Id).UseCollation("C");
        avatar.Property(e => e.UserId).UseCollation("C");
        avatar.Property(e => e.AvatarKey).UseCollation("C");

        var userPresence = model.Entity<DbUserPresence>();
        userPresence.Property(e => e.UserId).UseCollation("C");

        var passkey = model.Entity<DbPasskey>();
        passkey.Property(e => e.Id).UseCollation("C");
        passkey.Property(e => e.UserId).UseCollation("C");

        var chatPosition = model.Entity<DbChatPosition>();
        chatPosition.Property(e => e.Id).UseCollation("C");

        var userVoice = model.Entity<DbUserVoice>();
        userVoice.Property(e => e.Id).UseCollation("C");

        // AddUsage created both keys as pk_*, while a key declared after UseSnakeCaseNaming gets EF's PK_*
        var usageEvent = model.Entity<DbUsageEvent>();
        usageEvent.HasKey(e => new { e.UserId, e.OccurredAt, e.Kind, e.SourceId }).HasName("pk_usage_events");
        usageEvent.Property(e => e.UserId).UseCollation("C");
        usageEvent.Property(e => e.SourceId).UseCollation("C");

        var usageDay = model.Entity<DbUsageDay>();
        usageDay.HasKey(e => new { e.UserId, e.Day }).HasName("pk_usage_days");
        usageDay.Property(e => e.UserId).UseCollation("C");

        var coachEvent = model.Entity<DbCoachEvent>();
        coachEvent.HasKey(e => new { e.UserId, e.SourceId });
        coachEvent.Property(e => e.UserId).UseCollation("C");
        coachEvent.Property(e => e.SourceId).UseCollation("C");
        coachEvent.Property(e => e.ChatId).UseCollation("C");

        var coachDay = model.Entity<DbCoachDay>();
        coachDay.HasKey(e => new { e.UserId, e.Day, e.Language });
        coachDay.Property(e => e.UserId).UseCollation("C");
        coachDay.Property(e => e.Language).UseCollation("C");

        var userAttribution = model.Entity<DbUserAttribution>();
        userAttribution.Property(e => e.UserId).UseCollation("C");
        userAttribution.Property(e => e.DedupeKey).UseCollation("C");
        // At most one initial and one latest attribution per user; the index names keep the two apart
        userAttribution.HasIndex(e => e.UserId, "ix_user_attributions_initial").IsUnique().HasFilter("is_initial");
        userAttribution.HasIndex(e => e.UserId, "ix_user_attributions_latest").IsUnique().HasFilter("is_latest");

        var userMilestone = model.Entity<DbUserMilestone>();
        userMilestone.Property(e => e.UserId).UseCollation("C");

        var userMilestoneDelivery = model.Entity<DbUserMilestoneDelivery>();
        userMilestoneDelivery.Property(e => e.UserId).UseCollation("C");

        // One id per kind of device: the one seen last is the one milestones go out through
        var userAnalyticsClient = model.Entity<DbUserAnalyticsClient>();
        userAnalyticsClient.HasKey(e => new { e.UserId, e.Kind });
        userAnalyticsClient.Property(e => e.UserId).UseCollation("C");

        model.IgnoreUnusedOperationsFrameworkColumns(DbLogEntrySerializer.Default);

        var operation = model.Entity<DbOperation>();
        operation.Property(e => e.Uuid).UseCollation("C");
        operation.Property(e => e.HostId).UseCollation("C");

        var events = model.Entity<DbEvent>();
        events.Property(e => e.Uuid).UseCollation("C");
        events.DefineIndexes();
    }
}
