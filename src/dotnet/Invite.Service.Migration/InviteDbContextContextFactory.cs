using ActualChat.Invite.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Invite;

public class InviteDbContextContextFactory : IDesignTimeDbContextFactory<InviteDbContext>
{
    public string ConnectionString =
        "Server=localhost;Database=ac_dev_invite;Port=5432;User Id=postgres;Password=postgres";

    public InviteDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<InviteDbContext>();
        builder
            .UseNpgsql(
                ConnectionString,
                o => o.MigrationsAssembly(typeof(InviteDbContextContextFactory).Assembly.FullName)
            )
            .UseSnakeCaseNamingConvention();

        return new InviteDbContext(builder.Options);
    }
}
