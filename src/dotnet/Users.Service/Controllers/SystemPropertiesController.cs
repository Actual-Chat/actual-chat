using ActualChat.Hosting;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class SystemPropertiesController : ControllerBase, ISystemProperties
{
    private ISystemProperties Service { get; }

    public SystemPropertiesController(ISystemProperties service)
        => Service = service;

    [HttpGet]
    public Task<double> GetTime(CancellationToken cancellationToken)
        => Service.GetTime(cancellationToken);

    [HttpGet]
    public Task<string?> GetMinClientVersion(AppKind appKind, CancellationToken cancellationToken)
        => Service.GetMinClientVersion(appKind, cancellationToken);
}
