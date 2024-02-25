using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Search.Db;

public class SearchDbContext(DbContextOptions<SearchDbContext> options) : DbContextBase(options)
{
    public DbSet<DbIndexedChat> IndexedChats { get; protected set; } = null!;
    public DbSet<DbContactIndexState> ContactIndexStates { get; protected set; } = null!;
    // Stl.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;

    protected override void OnModelCreating(ModelBuilder model)
        => model.ApplyConfigurationsFromAssembly(typeof(SearchDbContext).Assembly).UseSnakeCaseNaming();
}
