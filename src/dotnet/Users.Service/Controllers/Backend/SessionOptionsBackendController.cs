using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class SessionOptionsBackendController : ControllerBase, ISessionOptionsBackend
{
    private readonly ISessionOptionsBackend _service;

    public SessionOptionsBackendController(ISessionOptionsBackend service)
        => _service = service;

    [HttpPost]
    public Task Upsert(
        [FromBody] ISessionOptionsBackend.UpsertCommand command,
        CancellationToken cancellationToken)
        => _service.Upsert(command, cancellationToken);
}
