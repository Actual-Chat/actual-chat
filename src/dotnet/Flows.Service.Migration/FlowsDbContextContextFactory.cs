using ActualChat.Flows.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Flows;

public class FlowsDbContextContextFactory : IDesignTimeDbContextFactory<FlowsDbContext>
{
    public string ConnectionString =
        "Server=localhost;Database=ac_dev_flows;Port=5432;User Id=postgres;Password=postgres;Include Error Detail=True";

    public FlowsDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<FlowsDbContext>();
        builder.UseNpgsql(
            ConnectionString,
            o => o.MigrationsAssembly(typeof(FlowsDbContextContextFactory).Assembly.FullName));

        return new FlowsDbContext(builder.Options);
    }
}
