using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class UserAvatarsController : ControllerBase, IUserAvatars
{
    private readonly IUserAvatars _service;

    public UserAvatarsController(IUserAvatars service) => _service = service;

    [HttpGet, Publish]
    public Task<UserAvatar?> Get(Session session, string avatarId, CancellationToken cancellationToken)
        => _service.Get(session, avatarId, cancellationToken);

    [HttpGet, Publish]
    public Task<string[]> GetAvatarIds(Session session, CancellationToken cancellationToken)
        => _service.GetAvatarIds(session, cancellationToken);

    [HttpGet, Publish]
    public Task<string> GetDefaultAvatarId(Session session, CancellationToken cancellationToken)
        => _service.GetDefaultAvatarId(session, cancellationToken);

    [HttpPost]
    public Task<UserAvatar> Create([FromBody] IUserAvatars.CreateCommand command, CancellationToken cancellationToken)
        => _service.Create(command, cancellationToken);

    [HttpPost]
    public Task Update(IUserAvatars.UpdateCommand command, CancellationToken cancellationToken)
        => _service.Update(command, cancellationToken);

    [HttpPost]
    public Task SetDefault([FromBody] IUserAvatars.SetDefaultCommand command, CancellationToken cancellationToken)
        => _service.SetDefault(command, cancellationToken);
}
