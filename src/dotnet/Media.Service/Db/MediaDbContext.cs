using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Media.Db;

public class MediaDbContext(DbContextOptions<MediaDbContext> options) : DbContextBase(options)
{
    public DbSet<DbMedia> Media { get; protected set; } = null!;
    public DbSet<DbLinkPreview> LinkPreviews { get; protected set; } = null!;

    // ActualLab.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;
    public DbSet<DbEvent> Events { get; protected set; } = null!;

    protected override void OnModelCreating(ModelBuilder model)
        => model.ApplyConfigurationsFromAssembly(typeof(MediaDbContext).Assembly).UseSnakeCaseNaming();
}
