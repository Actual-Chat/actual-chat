using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Users.Migrations;

public class UsersDbContextContextFactory : IDesignTimeDbContextFactory<UsersDbContext>
{
    public string UsePostgreSql =
            "Server=localhost;Database=ac_dev_users;Port=5432;User Id=postgres;Password=postgres";

    public UsersDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<UsersDbContext>();
        optionsBuilder.UseNpgsql(
            UsePostgreSql,
            o => o.MigrationsAssembly(typeof(UsersDbContextContextFactory).Assembly.FullName));

        return new UsersDbContext(optionsBuilder.Options);
    }
}
