using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Feedback.Db;

public class FeedbackDbContext(DbContextOptions<FeedbackDbContext> options) : DbContextBase(options)
{
    public DbSet<DbFeatureRequest> FeatureRequests { get; protected set; } = null!;

    // ActualLab.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;

    protected override void OnModelCreating(ModelBuilder model)
        => model.ApplyConfigurationsFromAssembly(typeof(FeedbackDbContext).Assembly).UseSnakeCaseNaming();
}
