using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Users.Migrations;

public class UsersDbContextContextFactory : IDesignTimeDbContextFactory<UsersDbContext>
{
    public string ConnectionString =
        "Server=localhost;Database=ac_dev_invite;Port=3306;User=root;Password=mariadb";

    public UsersDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<UsersDbContext>();
        builder.UseMySql(ConnectionString,
            ServerVersion.AutoDetect(ConnectionString),
            o => o.MigrationsAssembly(typeof(UsersDbContextContextFactory).Assembly.FullName));

        return new UsersDbContext(builder.Options);
    }
}
