using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class AvatarsController : ControllerBase, IAvatars
{
    private IAvatars Service { get; }
    private ICommander Commander { get; }

    public AvatarsController(IAvatars service, ICommander commander)
    {
        Service = service;
        Commander = commander;
    }

    [HttpGet, Publish]
    public Task<AvatarFull?> GetOwn(Session session, Symbol avatarId, CancellationToken cancellationToken)
        => Service.GetOwn(session, avatarId, cancellationToken);

    [HttpGet, Publish]
    public Task<Avatar?> Get(Session session, Symbol avatarId, CancellationToken cancellationToken)
        => Service.Get(session, avatarId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Symbol>> ListOwnAvatarIds(Session session, CancellationToken cancellationToken)
        => Service.ListOwnAvatarIds(session, cancellationToken);

    [HttpPost]
    public Task<AvatarFull> Change([FromBody] IAvatars.ChangeCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);

    [HttpPost]
    public Task SetDefault([FromBody] IAvatars.SetDefaultCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);
}
