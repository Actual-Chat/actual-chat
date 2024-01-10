using ActualChat.Search.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Search;

public class SearchDbContextContextFactory : IDesignTimeDbContextFactory<SearchDbContext>
{
    public string ConnectionString =
        "Server=localhost;Database=ac_dev_search;Port=5432;User Id=postgres;Password=postgres;Include Error Detail=True";

    public SearchDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<SearchDbContext>();
        builder.UseNpgsql(
            ConnectionString,
            o => o.MigrationsAssembly(typeof(SearchDbContextContextFactory).Assembly.FullName));

        return new SearchDbContext(builder.Options);
    }
}
