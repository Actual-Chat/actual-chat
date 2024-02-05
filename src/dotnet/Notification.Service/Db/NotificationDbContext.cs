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

    protected override void OnModelCreating(ModelBuilder model)
        => model.ApplyConfigurationsFromAssembly(typeof(NotificationDbContext).Assembly).UseSnakeCaseNaming();
}
