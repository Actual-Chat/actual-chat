using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Media.Db;

public class MediaDbContext : DbContextBase
{
    public DbSet<DbMedia> Media { get; protected set; } = null!;

    // Stl.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;

    public MediaDbContext(DbContextOptions<MediaDbContext> options) : base(options) { }

#pragma warning disable IL2026
    protected override void OnModelCreating(ModelBuilder model)
        => model.ApplyConfigurationsFromAssembly(typeof(MediaDbContext).Assembly).UseSnakeCaseNaming();
#pragma warning restore IL2026
}
