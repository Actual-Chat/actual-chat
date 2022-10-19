using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class ReactionsController : ControllerBase, IReactions
{
    private ICommander Commander { get; }
    private IReactions Service { get; }

    public ReactionsController(IReactions service, ICommander commander)
    {
        Commander = commander;
        Service = service;
    }

    [HttpGet, Publish]
    public Task<ImmutableArray<ReactionSummary>> List(
        Session session,
        Symbol chatEntryId,
        CancellationToken cancellationToken)
        => Service.List(session, chatEntryId, cancellationToken);

    [HttpPost]
    public Task React(IReactions.ReactCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);
}
