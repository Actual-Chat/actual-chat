using ActualChat.Notification.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Notification.Migrations;

public class NotificationDbContextContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public string UsePostgreSql =
            "Server=localhost;Database=ac_dev_notification;Port=5432;User Id=postgres;Password=postgres";

    public NotificationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<NotificationDbContext>();
        optionsBuilder.UseNpgsql(
            UsePostgreSql,
            o => o.MigrationsAssembly(typeof(NotificationDbContextContextFactory).Assembly.FullName));

        return new NotificationDbContext(optionsBuilder.Options);
    }
}
