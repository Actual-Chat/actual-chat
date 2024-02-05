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

    protected override void OnModelCreating(ModelBuilder model)
        => model.ApplyConfigurationsFromAssembly(typeof(InviteDbContext).Assembly).UseSnakeCaseNaming();
}
