using ActualChat.MLSearch.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.MLSearch;

public class MLSearchDbContextContextFactory : IDesignTimeDbContextFactory<MLSearchDbContext>
{
    public string ConnectionString =
        "Server=localhost;Database=ac_dev_mlsearch;Port=5432;User Id=postgres;Password=postgres;Include Error Detail=True";

    public MLSearchDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<MLSearchDbContext>();
        builder.UseNpgsql(
            ConnectionString,
            o => o.MigrationsAssembly(typeof(MLSearchDbContextContextFactory).Assembly.FullName));

        return new MLSearchDbContext(builder.Options);
    }
}
