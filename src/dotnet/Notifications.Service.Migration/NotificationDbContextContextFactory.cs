using ActualChat.Notifications.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Notifications;

public class NotificationDbContextContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public string ConnectionString =
        "Server=localhost;Database=ac_dev_notification;Port=5432;User Id=postgres;Password=postgres;Include Error Detail=True";

    public NotificationDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<NotificationDbContext>();
        builder.UseNpgsql(
            ConnectionString,
            o => o.MigrationsAssembly(typeof(NotificationDbContextContextFactory).Assembly.FullName));

        return new NotificationDbContext(builder.Options);
    }
}
