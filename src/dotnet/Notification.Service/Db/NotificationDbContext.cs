using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Notification.Db;

public class NotificationDbContext : DbContextBase
{
    public DbSet<DbDevice> Devices { get; protected set; } = null!;
    public DbSet<DbNotification> Notifications { get; protected set; } = null!;

    // Stl.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;

    public NotificationDbContext(DbContextOptions options) : base(options) { }

#pragma warning disable IL2026
    protected override void OnModelCreating(ModelBuilder model)
        => model.ApplyConfigurationsFromAssembly(typeof(NotificationDbContext).Assembly).UseSnakeCaseNaming();
#pragma warning restore IL2026
}
