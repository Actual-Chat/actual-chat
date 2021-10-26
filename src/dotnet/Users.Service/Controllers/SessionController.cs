using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
[Internal]
public class SessionController : ControllerBase, ISessionInfoService
{
    private readonly ISessionResolver _sessionResolver;
    private readonly ISessionInfoService _sessionService;

    public SessionController(ISessionResolver sessionResolver, ISessionInfoService sessionService)
    {
        _sessionResolver = sessionResolver;
        _sessionService = sessionService;
    }

    [HttpPost]
    public Task Update([FromBody] ISessionInfoService.UpsertData command, CancellationToken cancellationToken)
    {
        command.UseDefaultSession(_sessionResolver);
        return _sessionService.Update(command, cancellationToken);
    }
}

