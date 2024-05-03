using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Invite.Db;

public class InviteDbContext(DbContextOptions<InviteDbContext> options) : DbContextBase(options)
{
    public DbSet<DbInvite> Invites { get; protected set; } = null!;
    public DbSet<DbActivationKey> ActivationKeys { get; protected set; } = null!;

    // ActualLab.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;
    public DbSet<DbEvent> Events { get; protected set; } = null!;

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ApplyConfigurationsFromAssembly(typeof(InviteDbContext).Assembly).UseSnakeCaseNaming();

        var invite = model.Entity<DbInvite>();
        invite.Property(e => e.Id).UseCollation("C");
        invite.Property(e => e.SearchKey).UseCollation("C");
        invite.Property(e => e.CreatedBy).UseCollation("C");

        var activationKey = model.Entity<DbActivationKey>();
        activationKey.Property(e => e.Id).UseCollation("C");

        var operation = model.Entity<DbOperation>();
        operation.Property(e => e.Uuid).UseCollation("C");
        operation.Property(e => e.HostId).UseCollation("C");

        var events = model.Entity<DbEvent>();
        events.Property(e => e.Uuid).UseCollation("C");
    }
}
