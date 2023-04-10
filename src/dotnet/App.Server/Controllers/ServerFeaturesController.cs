using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.App.Server.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public sealed class ServerFeaturesController : ControllerBase, IServerFeatures
{
    protected IServerFeatures Service { get; }

    public IServiceProvider Services { get; }

    public ServerFeaturesController(IServiceProvider services)
    {
        Services = services;
        Service = services.GetRequiredService<IServerFeatures>();
    }

    [HttpGet]
    public Task<object?> Get(Type featureType, CancellationToken cancellationToken)
        => throw StandardError.NotSupported("Only GetJson method is supported by this controller.");

    [HttpGet, Publish]
    public Task<string> GetJson(TypeRef featureTypeRef, CancellationToken cancellationToken)
        => Service.GetJson(featureTypeRef, cancellationToken);
}
