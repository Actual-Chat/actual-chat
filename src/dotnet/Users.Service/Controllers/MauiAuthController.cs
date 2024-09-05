using ActualChat.Security;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Users.Controllers;

// NOTE(AY): All requests to this controller must be opened in browser to rather than called via RestEase!
[ApiController, Route(Route)]
public sealed class MauiAuthController(IServiceProvider services) : ControllerBase
{
    public const string Route = "/maui-auth";

    private ISecureTokensBackend SecureTokensBackend { get; } = services.GetRequiredService<ISecureTokensBackend>();
    private ServerAuth ServerAuth { get; } = services.GetRequiredService<ServerAuth>();
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

    // 2024.08: All methods below are obsolete

    [HttpGet("sign-in/{scheme}")]
    [Obsolete("2024.08: 'Start' is the only method used by MAUI clients now.")]
    public ActionResult SignIn(
        string scheme, [FromQuery(Name = "s")] string sessionToken, string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        var session = SecureTokensBackend.ParseSessionToken(sessionToken);
        HttpContext.AddSessionCookie(session);
        if (returnUrl.IsNullOrEmpty())
            returnUrl = UrlMapper.ToAbsolute(Links.CloseFlow("Sign-in", false));
        var syncUrl = UrlMapper.ToAbsolute(
            $"{Route}/sync?s={sessionToken.UrlEncode()}&returnUrl={returnUrl.UrlEncode()}");
        return Redirect($"/signIn/{scheme}?returnUrl={syncUrl.UrlEncode()}");
    }

    [HttpGet("sign-out")]
    [Obsolete("2024.08: 'Start' is the only method used by MAUI clients now.")]
    public ActionResult SignOut(
        [FromQuery(Name = "s")] string sessionToken, string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        var session = SecureTokensBackend.ParseSessionToken(sessionToken);
        HttpContext.AddSessionCookie(session);
        if (returnUrl.IsNullOrEmpty())
            returnUrl = UrlMapper.ToAbsolute(Links.CloseFlow("Sign-out", false));
        var syncUrl = UrlMapper.ToAbsolute(
            $"{Route}/sync?s={sessionToken.UrlEncode()}&returnUrl={returnUrl.UrlEncode()}");
        return Redirect($"/signOut?returnUrl={syncUrl.UrlEncode()}");
    }

    [HttpGet("sync")]
    [Obsolete("2024.08: 'Start' is the only method used by MAUI clients now.")]
    public async Task<ActionResult> Sync(
        [FromQuery(Name = "s")] string sessionToken, string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        var session = SecureTokensBackend.ParseSessionToken(sessionToken);
        await ServerAuth.UpdateAuthState(session, HttpContext, true, cancellationToken).ConfigureAwait(false);
        returnUrl = returnUrl.NullIfEmpty() ?? Links.CloseFlow("Authentication state update").Value;
        return Redirect(returnUrl);
    }
}
