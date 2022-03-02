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
    private readonly ISessionResolver _sessionResolver;

    public FeedbacksController(IFeedbacks service, ISessionResolver sessionResolver)
    {
        _service = service;
        _sessionResolver = sessionResolver;
    }

    public Task CreateFeatureRequest(IFeedbacks.FeatureRequestCommand command, CancellationToken cancellationToken)
    {
        command.UseDefaultSession(_sessionResolver);
        return _service.CreateFeatureRequest(command, cancellationToken);
    }
}
