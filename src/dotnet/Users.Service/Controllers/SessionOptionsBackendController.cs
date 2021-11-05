using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
[Internal]
public class SessionOptionsBackendController : ControllerBase, ISessionOptionsBackend
{
    private readonly ISessionOptionsBackend _service;

    public SessionOptionsBackendController(ISessionOptionsBackend service)
        => _service = service;

    [HttpPost]
    public Task Update(
        [FromBody] ISessionOptionsBackend.UpdateCommand command,
        CancellationToken cancellationToken)
        => _service.Update(command, cancellationToken);
}
