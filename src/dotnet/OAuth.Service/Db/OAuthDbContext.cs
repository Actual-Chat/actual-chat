using ActualChat.Db;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.OAuth.Db;

public class OAuthDbContext(DbContextOptions<OAuthDbContext> options) : DbContextBase(options)
{
    // ActualLab.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;
    public DbSet<DbEvent> Events { get; protected set; } = null!;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Conventions.Add(_ => new RemoveDbEventIndexesConvention());
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.UseOpenIddict();
        model.ApplyConfigurationsFromAssembly(typeof(OAuthDbContext).Assembly).UseSnakeCaseNaming();

        var operation = model.Entity<DbOperation>();
        operation.Property(e => e.Uuid).UseCollation("C");
        operation.Property(e => e.HostId).UseCollation("C");

        var events = model.Entity<DbEvent>();
        events.Property(e => e.Uuid).UseCollation("C");
        events.DefineIndexes();
    }
}
