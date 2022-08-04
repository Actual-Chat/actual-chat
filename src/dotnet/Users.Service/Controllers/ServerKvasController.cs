using ActualChat.Kvas;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class ServerKvasController : ControllerBase, IServerKvas
{
    private readonly IServerKvas _service;
    private readonly ICommander _commander;

    public ServerKvasController(IServerKvas service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet, Publish]
    public Task<string?> Get(Session session, string key, CancellationToken cancellationToken = default)
        => _service.Get(session, key, cancellationToken);

    [HttpPost]
    public Task Set([FromBody] IServerKvas.SetCommand command, CancellationToken cancellationToken = default)
        => _service.Set(command, cancellationToken);

    [HttpPost]
    public Task SetMany([FromBody] IServerKvas.SetManyCommand command, CancellationToken cancellationToken = default)
        => _service.SetMany(command, cancellationToken);
}
