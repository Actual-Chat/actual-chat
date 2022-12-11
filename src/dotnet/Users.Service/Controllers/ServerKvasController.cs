using ActualChat.Kvas;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class ServerKvasController : ControllerBase, IServerKvas
{
    private IServerKvas Service { get; }
    private ICommander Commander { get; }

    public ServerKvasController(IServerKvas service, ICommander commander)
    {
        Service = service;
        Commander = commander;
    }

    [HttpGet, Publish]
    public Task<Option<string>> Get(Session session, string key, CancellationToken cancellationToken = default)
        => Service.Get(session, key, cancellationToken);

    [HttpPost]
    public Task Set([FromBody] IServerKvas.SetCommand command, CancellationToken cancellationToken = default)
        => Commander.Call(command, cancellationToken);

    [HttpPost]
    public Task SetMany([FromBody] IServerKvas.SetManyCommand command, CancellationToken cancellationToken = default)
        => Commander.Call(command, cancellationToken);

    [HttpPost]
    public Task MigrateGuestKeys([FromBody] IServerKvas.MigrateGuestKeysCommand command, CancellationToken cancellationToken = default)
        => Commander.Call(command, cancellationToken);
}
