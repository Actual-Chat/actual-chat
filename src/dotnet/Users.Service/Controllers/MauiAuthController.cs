using ActualChat.Users.Module;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Users.Controllers;

// NOTE(AY): All requests to this controller must be opened in a browser rather than called via RestEase!
[ApiController, Route(Route)]
public sealed class MauiAuthController(IServiceProvider services) : ControllerBase
{
    public const string Route = "/maui-auth";
    private const string FetchSiteHeaderName = "Sec-Fetch-Site";
    private const string AppInitiatedFetchSite = "none";

    // Everything MauiAccountUI has ever passed as "e". /signIn is Fusion's default-scheme route;
    // /signOut is what pre-2026-07 clients still send. Anything else has no reason to be handed
    // a session token cookie.
    private static readonly IReadOnlySet<string> AllowedEndpoints =
        AuthSchema.AllExternal
            .Select(x => $"/signIn/{x}")
            .Concat(["/signIn", "/signOut"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private UrlMapper UrlMapper { get; } = services.UrlMapper();
    private HostInfo HostInfo { get; } = services.HostInfo();
    private UsersSettings Settings { get; } = services.GetRequiredService<UsersSettings>();
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
        // The app always starts this flow by handing the URL to a browser component, which makes it
        // an initiator-less navigation. A document-initiated one didn't come from the app.
        var fetchSite = Request.Headers[FetchSiteHeaderName].FirstOrDefault();
        if (Settings.IsMauiAuthFetchSiteCheckEnabled
            && !fetchSite.IsNullOrEmpty()
            && !string.Equals(fetchSite, AppInitiatedFetchSite, StringComparison.OrdinalIgnoreCase)) {
            Log.LogWarning("Start: rejected a {FetchSiteHeader}: {FetchSite} navigation",
                FetchSiteHeaderName, fetchSite);
            return BadRequest("Invalid request.");
        }

        if (!endpoint.StartsWith('/'))
            endpoint = $"/{endpoint}";
        if (!AllowedEndpoints.Contains(endpoint)) {
            Log.LogWarning("Start: rejected endpoint '{Endpoint}'", endpoint);
            return BadRequest("Invalid endpoint.");
        }

        // Store the secure session token as a cookie — it will be picked up by AuthHelper
        // on signIn/signOut/close flows and removed on close flow.
        // We never store the raw session ID in a cookie here to prevent MAUI sessions leaking to the browser.
        HttpContext.AddSessionTokenCookie(sessionToken);
        var baseUrl = HostInfo.GetAllowedBaseUrl(Request.Host.Host);
        // Windows has no in-app browser, so it needs the close page to actually render (and
        // script-navigate to the app scheme) rather than 302 straight to it — see CloseFlow.
        var mustClose = Enum.TryParse<AppKind>(appKind, true, out var parsedAppKind)
            && parsedAppKind == AppKind.Windows;
        var closeFlowUrl = UrlMapper.ToAbsolute(baseUrl,
            Links.CloseFlow(flowName, mustClose, redirectUrl));
        return Redirect($"{endpoint}?returnUrl={closeFlowUrl.UrlEncode()}");
    }
}
