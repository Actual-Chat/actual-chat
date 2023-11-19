using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Feedback.Db;

public class FeedbackDbContext : DbContextBase
{
    public DbSet<DbFeatureRequest> FeatureRequests { get; protected set; } = null!;

    // Stl.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;

    public FeedbackDbContext(DbContextOptions<FeedbackDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder model)
        => model.ApplyConfigurationsFromAssembly(typeof(FeedbackDbContext).Assembly).UseSnakeCaseNaming();
}
