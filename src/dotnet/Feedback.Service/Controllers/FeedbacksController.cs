using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Feedback.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class FeedbacksController : ControllerBase, IFeedbacks
{
    private IFeedbacks Service { get; }
    private ICommander Commander { get; }

    public FeedbacksController(IFeedbacks service, ICommander commander)
    {
        Service = service;
        Commander = commander;
    }

    public Task CreateFeatureRequest(IFeedbacks.FeatureRequestCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);
}
