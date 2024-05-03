using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Search.Db;

public class SearchDbContext(DbContextOptions<SearchDbContext> options) : DbContextBase(options)
{
    public DbSet<DbIndexedChat> IndexedChats { get; protected set; } = null!;
    public DbSet<DbContactIndexState> ContactIndexStates { get; protected set; } = null!;

    // ActualLab.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;
    public DbSet<DbEvent> Events { get; protected set; } = null!;

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ApplyConfigurationsFromAssembly(typeof(SearchDbContext).Assembly).UseSnakeCaseNaming();

        var indexedChat = model.Entity<DbIndexedChat>();
        indexedChat.Property(e => e.Id).UseCollation("C");

        var indexedState = model.Entity<DbContactIndexState>();
        indexedState.Property(e => e.Id).UseCollation("C");
        indexedState.Property(e => e.LastUpdatedId).UseCollation("C");

        var operation = model.Entity<DbOperation>();
        operation.Property(e => e.Uuid).UseCollation("C");
        operation.Property(e => e.HostId).UseCollation("C");

        var events = model.Entity<DbEvent>();
        events.Property(e => e.Uuid).UseCollation("C");
    }
}
