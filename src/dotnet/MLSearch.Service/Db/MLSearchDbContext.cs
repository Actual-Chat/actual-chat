using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.MLSearch.Db;

public class MLSearchDbContext(DbContextOptions<MLSearchDbContext> options) : DbContextBase(options)
{
    // Stl.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;

    protected override void OnModelCreating(ModelBuilder model)
        => model.ApplyConfigurationsFromAssembly(typeof(MLSearchDbContext).Assembly).UseSnakeCaseNaming();
}
