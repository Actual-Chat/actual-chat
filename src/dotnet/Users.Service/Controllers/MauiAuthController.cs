using ActualChat.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Users.Controllers;

// NOTE(AY): All requests to this controller must be opened in a browser rather than called via RestEase!
[ApiController, Route(Route)]
public sealed class MauiAuthController(IServiceProvider services) : ControllerBase
{
    public const string Route = "/maui-auth";

    private UrlMapper UrlMapper { get; } = services.UrlMapper();
    private HostInfo HostInfo { get; } = services.HostInfo();
    private ILogger Log { get; } = services.LogFor<MauiAuthController>();

    [HttpGet("start")]
    public ActionResult Start(
        [FromQuery(Name = "s")] string sessionToken,
        [FromQuery(Name = "e")] string endpoint,
        [FromQuery(Name = "flow")] string flowName,
        string? redirectUrl = null,
        int mustExist = 0,
        CancellationToken cancellationToken = default)
    {
        // Store the secure session token as a cookie — it will be picked up by AuthHelper
        // on signIn/signOut/close flows and removed on close flow.
        // We never store the raw session ID in a cookie here to prevent MAUI sessions leaking to the browser.
        HttpContext.AddSessionTokenCookie(sessionToken);
        var baseUrl = HostInfo.GetAllowedBaseUrl(Request.Host.Host);
        var closeFlowUrl = UrlMapper.ToAbsolute(baseUrl,
            Links.CloseFlow(flowName, false, mustExist != 0, redirectUrl));
        if (!endpoint.StartsWith('/'))
            endpoint = $"/{endpoint}";
        return Redirect($"{endpoint}?returnUrl={closeFlowUrl.UrlEncode()}");
    }
}
