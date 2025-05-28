using ActualChat.Security;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Users.Controllers;

// NOTE(AY): All requests to this controller must be opened in a browser rather than called via RestEase!
[ApiController, Route(Route)]
public sealed class MauiAuthController(IServiceProvider services) : ControllerBase
{
    public const string Route = "/maui-auth";

    private ISecureTokensBackend SecureTokensBackend { get; } = services.GetRequiredService<ISecureTokensBackend>();
    private UrlMapper UrlMapper { get; } = services.UrlMapper();
    private ILogger Log { get; } = services.LogFor<MauiAuthController>();

    [HttpGet("start")]
    public ActionResult Start(
        [FromQuery(Name = "s")] string sessionToken,
        [FromQuery(Name = "e")] string endpoint,
        [FromQuery(Name = "flow")] string flowName,
        string? redirectUrl = null,
        CancellationToken cancellationToken = default)
    {
        var session = SecureTokensBackend.ParseSessionToken(sessionToken);
        HttpContext.AddSessionCookie(session);
        var closeFlowUrl = UrlMapper.ToAbsolute(Links.CloseFlow(flowName, false, redirectUrl));
        if (!endpoint.OrdinalStartsWith("/"))
            endpoint = $"/{endpoint}";
        return Redirect($"{endpoint}?returnUrl={closeFlowUrl.UrlEncode()}");
    }
}
