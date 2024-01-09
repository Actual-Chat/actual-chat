using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Media.Db;

public class MediaDbContext : DbContextBase
{
    public DbSet<DbMedia> Media { get; protected set; } = null!;
    public DbSet<DbLinkPreview> LinkPreviews { get; protected set; } = null!;

    // ActualLab.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;

    public MediaDbContext(DbContextOptions<MediaDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder model)
        => model.ApplyConfigurationsFromAssembly(typeof(MediaDbContext).Assembly).UseSnakeCaseNaming();
}
