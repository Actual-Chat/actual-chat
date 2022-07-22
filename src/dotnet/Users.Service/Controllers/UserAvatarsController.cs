using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class UserAvatarsController : ControllerBase, IUserAvatars
{
    private readonly IUserAvatars _service;
    private readonly ICommander _commander;

    public UserAvatarsController(IUserAvatars service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet, Publish]
    public Task<UserAvatar?> Get(Session session, string avatarId, CancellationToken cancellationToken)
        => _service.Get(session, avatarId, cancellationToken);

    [HttpGet, Publish]
    public Task<Symbol> GetDefaultAvatarId(Session session, CancellationToken cancellationToken)
        => _service.GetDefaultAvatarId(session, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Symbol>> ListAvatarIds(Session session, CancellationToken cancellationToken)
        => _service.ListAvatarIds(session, cancellationToken);

    [HttpPost]
    public Task<UserAvatar> Create([FromBody] IUserAvatars.CreateCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);

    [HttpPost]
    public Task Update([FromBody] IUserAvatars.UpdateCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);

    [HttpPost]
    public Task SetDefault([FromBody] IUserAvatars.SetDefaultCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
