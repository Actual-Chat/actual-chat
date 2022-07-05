using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Feedback.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class FeedbacksController : ControllerBase, IFeedbacks
{
    private readonly IFeedbacks _service;
    private readonly ICommander _commander;

    public FeedbacksController(IFeedbacks service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    public Task CreateFeatureRequest(IFeedbacks.FeatureRequestCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
