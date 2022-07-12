using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class SessionOptionsBackendController : ControllerBase, ISessionOptionsBackend
{
    private readonly ISessionOptionsBackend _service;
    private readonly ICommander _commander;

    public SessionOptionsBackendController(ISessionOptionsBackend service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpPost]
    public Task Upsert(
        [FromBody] ISessionOptionsBackend.UpsertCommand command,
        CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
