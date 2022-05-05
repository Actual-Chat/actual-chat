using ActualChat.Notification.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Notification;

public class NotificationDbContextContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public string ConnectionString =
        "Server=localhost;Database=ac_dev_invite;Port=3306;User=root;Password=mariadb";

    public NotificationDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<NotificationDbContext>();
        builder.UseMySql(ConnectionString,
            ServerVersion.AutoDetect(ConnectionString),
            o => o.MigrationsAssembly(typeof(NotificationDbContextContextFactory).Assembly.FullName));

        return new NotificationDbContext(builder.Options);
    }
}
