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
    {
        model.ApplyConfigurationsFromAssembly(typeof(MediaDbContext).Assembly).UseSnakeCaseNaming();

        var media = model.Entity<DbMedia>();
        media.Property(e => e.Id).UseCollation("C");
        media.Property(e => e.Scope).UseCollation("C");
        media.Property(e => e.LocalId).UseCollation("C");
        media.Property(e => e.ContentId).UseCollation("C");

        var linkPreview = model.Entity<DbLinkPreview>();
        linkPreview.Property(e => e.Id).UseCollation("C");
        linkPreview.Property(e => e.ThumbnailMediaId).UseCollation("C");

        var operation = model.Entity<DbOperation>();
        operation.Property(e => e.Uuid).UseCollation("C");
        operation.Property(e => e.HostId).UseCollation("C");

        var events = model.Entity<DbEvent>();
        events.Property(e => e.Uuid).UseCollation("C");
    }
}
