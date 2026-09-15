using ActualChat.OAuth.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.OAuth;

public class OAuthDbContextContextFactory : IDesignTimeDbContextFactory<OAuthDbContext>
{
    public string ConnectionString =
        "Server=127.0.0.1;Database=ac_dev_oauth;Port=5432;User Id=postgres;Password=postgres;Include Error Detail=True";

    public OAuthDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<OAuthDbContext>();
        builder.UseNpgsql(
            ConnectionString,
            o => o.MigrationsAssembly(typeof(OAuthDbContextContextFactory).Assembly.FullName));

        return new OAuthDbContext(builder.Options);
    }
}
