using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Users;

public class UsersDbContextContextFactory : IDesignTimeDbContextFactory<UsersDbContext>
{
    public string ConnectionString =
        "Server=localhost;Database=ac_dev_invite;Port=5432;User Id=postgres;Password=postgres";

    public UsersDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<UsersDbContext>();
        builder.UseNpgsql(
            ConnectionString,
            o => o.MigrationsAssembly(typeof(UsersDbContextContextFactory).Assembly.FullName));

        return new UsersDbContext(builder.Options);
    }
}
