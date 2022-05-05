using ActualChat.Invite.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Invite;

public class InviteDbContextContextFactory : IDesignTimeDbContextFactory<InviteDbContext>
{
    public string ConnectionString =
        "Server=localhost;Database=ac_dev_invite;Port=3306;User=root;Password=mariadb";

    public InviteDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<InviteDbContext>();
        builder.UseMySql(ConnectionString,
            ServerVersion.AutoDetect(ConnectionString),
            o => o.MigrationsAssembly(typeof(InviteDbContextContextFactory).Assembly.FullName));

        return new InviteDbContext(builder.Options);
    }
}
