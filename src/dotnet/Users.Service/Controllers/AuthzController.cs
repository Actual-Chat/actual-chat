using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class AuthzController : ControllerBase, IAuthz
{
    private readonly IAuthz _service;

    public AuthzController(IAuthz service)
        => _service = service;

    [HttpGet, Publish]
    public Task<bool> IsActive(Session session, CancellationToken cancellationToken)
        => _service.IsActive(session, cancellationToken);
}
