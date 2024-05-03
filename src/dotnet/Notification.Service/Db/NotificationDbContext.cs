using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Notification.Db;

public class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContextBase(options)
{
    public DbSet<DbDevice> Devices { get; protected set; } = null!;
    public DbSet<DbNotification> Notifications { get; protected set; } = null!;

    // ActualLab.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;
    public DbSet<DbEvent> Events { get; protected set; } = null!;

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ApplyConfigurationsFromAssembly(typeof(NotificationDbContext).Assembly).UseSnakeCaseNaming();

        var device = model.Entity<DbDevice>();
        device.Property(e => e.Id).UseCollation("C");
        device.Property(e => e.UserId).UseCollation("C");

        var notification = model.Entity<DbNotification>();
        notification.Property(e => e.Id).UseCollation("C");
        notification.Property(e => e.UserId).UseCollation("C");
        notification.Property(e => e.ChatId).UseCollation("C");
        notification.Property(e => e.AuthorId).UseCollation("C");
        notification.Property(e => e.SimilarityKey).UseCollation("C");

        var operation = model.Entity<DbOperation>();
        operation.Property(e => e.Uuid).UseCollation("C");
        operation.Property(e => e.HostId).UseCollation("C");

        var events = model.Entity<DbEvent>();
        events.Property(e => e.Uuid).UseCollation("C");
    }
}
