using ActualChat.Media.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Media;

public class MediaDbContextContextFactory : IDesignTimeDbContextFactory<MediaDbContext>
{
    public string ConnectionString =
        "Server=localhost;Database=ac_dev_media;Port=5432;User Id=postgres;Password=postgres;Include Error Detail=True";

    public MediaDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<MediaDbContext>();
        builder.UseNpgsql(
            ConnectionString,
            o => o.MigrationsAssembly(typeof(MediaDbContextContextFactory).Assembly.FullName));

        return new MediaDbContext(builder.Options);
    }
}
