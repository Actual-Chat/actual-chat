using ActualChat.Db;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

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
        // CIMD client ids are URLs; OpenIddict's default of 100 is sized for opaque ids
        model.Entity<OpenIddictEntityFrameworkCoreApplication>().Property(a => a.ClientId).HasMaxLength(1024);
        // This assembly is Users.Service, which also holds UsersDbContext's entity configurations -
        // an unfiltered scan would pull DbAccount & Co. into the OAuth model. Scan OAuth types only.
        model.ApplyConfigurationsFromAssembly(
                typeof(OAuthDbContext).Assembly,
                t => t.Namespace?.StartsWith("ActualChat.OAuth", StringComparison.Ordinal) == true)
            .UseSnakeCaseNaming();

        var operation = model.Entity<DbOperation>();
        operation.Property(e => e.Uuid).UseCollation("C");
        operation.Property(e => e.HostId).UseCollation("C");

        var events = model.Entity<DbEvent>();
        events.Property(e => e.Uuid).UseCollation("C");
        events.DefineIndexes();
    }
}
