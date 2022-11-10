using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Invite.Db;

public class InviteDbContext : DbContextBase
{
    public DbSet<DbInvite> Invites { get; protected set; } = null!;

    // Stl.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;

    public InviteDbContext(DbContextOptions options) : base(options) { }

#pragma warning disable IL2026
    protected override void OnModelCreating(ModelBuilder model)
        => model.ApplyConfigurationsFromAssembly(typeof(InviteDbContext).Assembly).UseSnakeCaseNaming();
#pragma warning restore IL2026
}
