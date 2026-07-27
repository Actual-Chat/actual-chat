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
        string? appKind = null,
        CancellationToken cancellationToken = default)
    {
        // Store the secure session token as a cookie — it will be picked up by AuthHelper
        // on signIn/signOut/close flows and removed on close flow.
        // We never store the raw session ID in a cookie here to prevent MAUI sessions leaking to the browser.
        HttpContext.AddSessionTokenCookie(sessionToken);
        var baseUrl = HostInfo.GetAllowedBaseUrl(Request.Host.Host);
        // Windows has no in-app browser, so it needs the close page to actually render (and
        // script-navigate to the app scheme) rather than 302 straight to it — see CloseFlow.
        var mustClose = Enum.TryParse<AppKind>(appKind, true, out var parsedAppKind) && parsedAppKind == AppKind.Windows;
        var closeFlowUrl = UrlMapper.ToAbsolute(baseUrl,
            Links.CloseFlow(flowName, mustClose, redirectUrl));
        if (!endpoint.StartsWith('/'))
            endpoint = $"/{endpoint}";
        return Redirect($"{endpoint}?returnUrl={closeFlowUrl.UrlEncode()}");
    }
}
