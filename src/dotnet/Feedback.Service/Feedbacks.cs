using ActualChat.Feedback.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Feedback;

public class Feedbacks : DbServiceBase<FeedbackDbContext>, IFeedbacks
{
    private readonly IAuth _auth;

    public Feedbacks(IServiceProvider services, IAuth auth) : base(services)
        => _auth = auth;

    public virtual async Task OnCreateFeatureRequest(Feedbacks_FeatureRequest command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var (session, feature) = command;
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        var userId = user?.Id.Value ?? "";

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbFeatureRequest = new DbFeatureRequest {
            Id = Ulid.NewUlid().ToString(),
            FeatureName = feature,
            UserId = userId,
            SessionId = session.Id,
            CreatedAt = Clocks.SystemClock.Now,
            Version = VersionGenerator.NextVersion(),
            Rating = command.Rating,
            Comment = command.Comment
        };

        dbContext.Add(dbFeatureRequest);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
