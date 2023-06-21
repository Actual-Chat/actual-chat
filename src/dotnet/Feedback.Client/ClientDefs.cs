using RestEase;

namespace ActualChat.Feedback;

[BasePath("feedbacks")]
public interface IFeedbacksClientDef
{
    [Post(nameof(CreateFeatureRequest))]
    Task CreateFeatureRequest([Body] Feedbacks_FeatureRequest command, CancellationToken cancellationToken);
}
