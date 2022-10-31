using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class AvatarsController : ControllerBase, IAvatars
{
    private readonly IAvatars _service;
    private readonly ICommander _commander;

    public AvatarsController(IAvatars service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet, Publish]
    public Task<AvatarFull?> GetOwn(Session session, string avatarId, CancellationToken cancellationToken)
        => _service.GetOwn(session, avatarId, cancellationToken);

    [HttpGet, Publish]
    public Task<Avatar?> Get(Session session, string avatarId, CancellationToken cancellationToken)
        => _service.Get(session, avatarId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Symbol>> ListOwnAvatarIds(Session session, CancellationToken cancellationToken)
        => _service.ListOwnAvatarIds(session, cancellationToken);

    [HttpPost]
    public Task<AvatarFull> Change([FromBody] IAvatars.ChangeCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);

    [HttpPost]
    public Task SetDefault([FromBody] IAvatars.SetDefaultCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
