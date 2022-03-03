using RestEase;

namespace ActualChat.Feedback.Client;

[BasePath("feedbacks")]
public interface IFeedbacksClientDef
{
    [Post(nameof(CreateFeatureRequest))]
    Task CreateFeatureRequest([Body] IFeedbacks.FeatureRequestCommand command, CancellationToken cancellationToken);
}
