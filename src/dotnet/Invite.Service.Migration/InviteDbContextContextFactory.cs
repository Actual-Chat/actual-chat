using ActualChat.Invite.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Invite;

public class InviteDbContextContextFactory : IDesignTimeDbContextFactory<InviteDbContext>
{
    public string UsePostgreSql =
            "Server=localhost;Database=ac_dev_invite;Port=5432;User Id=postgres;Password=postgres";

    public InviteDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<InviteDbContext>();
        optionsBuilder.UseNpgsql(
            UsePostgreSql,
            o => o.MigrationsAssembly(typeof(InviteDbContextContextFactory).Assembly.FullName));

        return new InviteDbContext(optionsBuilder.Options);
    }
}
