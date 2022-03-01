using ActualChat.Feedback.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Feedback;

public class FeedbackService : DbServiceBase<FeedbackDbContext>, IFeedback
{
    private readonly IAuth _auth;

    public FeedbackService(IServiceProvider services, IAuth auth) : base(services)
        => _auth = auth;

    public virtual async Task CreateFeatureRequest(IFeedback.FeatureRequestCommand command, CancellationToken cancellationToken)
    {
        var (session, feature) = command;
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        var userId = user.IsAuthenticated ? user.Id.ToString() : "";

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbFeatureRequest = new DbFeatureRequest {
            Id = Ulid.NewUlid().ToString(),
            FeatureName = command.Feature,
            UserId = userId,
            SessionId = session.Id,
            CreatedAt = Clocks.SystemClock.Now,
            Version = VersionGenerator.NextVersion(),
            Rating = command.Rating,
            AnswerId = command.AnswerId,
            Comment = command.Comment
        };

        dbContext.Add(dbFeatureRequest);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
