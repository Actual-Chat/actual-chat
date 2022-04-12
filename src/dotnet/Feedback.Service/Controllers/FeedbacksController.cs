using ActualChat.Feedback;
using ActualChat.Users;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Feedback.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class FeedbacksController : ControllerBase, IFeedbacks
{
    private readonly IFeedbacks _service;

    public FeedbacksController(IFeedbacks service)
        => _service = service;

    public Task CreateFeatureRequest(IFeedbacks.FeatureRequestCommand command, CancellationToken cancellationToken)
        => _service.CreateFeatureRequest(command, cancellationToken);
}
