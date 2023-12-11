using ActualChat.Security;
using ActualChat.Web;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server.Authentication;

namespace ActualChat.Users.Controllers;

// NOTE(AY): All requests to this controller must be opened in browser to rather than called via RestEase!
[ApiController, Route(Route)]
public sealed class MauiAuthController(IServiceProvider services) : ControllerBase
{
    public const string Route = "/maui-auth";

    private ISecureTokensBackend? _secureTokensBackend;
    private ServerAuthHelper? _serverAuthHelper;
    private UrlMapper? _urlMapper;
    private ILogger? _log;

    private IServiceProvider Services { get; } = services;
    private ISecureTokensBackend SecureTokensBackend => _secureTokensBackend ??= Services.GetRequiredService<ISecureTokensBackend>();
    private ServerAuthHelper ServerAuthHelper => _serverAuthHelper ??= Services.GetRequiredService<ServerAuthHelper>();
    private UrlMapper UrlMapper => _urlMapper ??= Services.GetRequiredService<UrlMapper>();
    private ILogger Log => _log ??= Services.LogFor(GetType());

    [HttpGet("sign-in/{scheme}")]
    public ActionResult SignIn(
        string scheme, [FromQuery(Name = "s")] string sessionToken, string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        var session = SecureTokensBackend.ParseSessionToken(sessionToken);
        HttpContext.AddSessionCookie(session);
        if (returnUrl.IsNullOrEmpty())
            returnUrl = UrlMapper.ToAbsolute(Links.AutoClose("Sign-in"));
        var syncUrl = UrlMapper.ToAbsolute(
            $"{Route}/sync?s={sessionToken.UrlEncode()}&returnUrl={returnUrl.UrlEncode()}");
        return Redirect($"/signIn/{scheme}?returnUrl={syncUrl.UrlEncode()}");
    }

    [HttpGet("sign-out")]
    public ActionResult SignOut(
        [FromQuery(Name = "s")] string sessionToken, string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        var session = SecureTokensBackend.ParseSessionToken(sessionToken);
        HttpContext.AddSessionCookie(session);
        if (returnUrl.IsNullOrEmpty())
            returnUrl = UrlMapper.ToAbsolute(Links.AutoClose("Sign-out"));
        var syncUrl = UrlMapper.ToAbsolute(
            $"{Route}/sync?s={sessionToken.UrlEncode()}&returnUrl={returnUrl.UrlEncode()}");
        return Redirect($"/signOut?returnUrl={syncUrl.UrlEncode()}");
    }

    [HttpGet("sync")]
    public async Task<ActionResult> Sync(
        [FromQuery(Name = "s")] string sessionToken, string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        var session = SecureTokensBackend.ParseSessionToken(sessionToken);
        await ServerAuthHelper.UpdateAuthState(session, HttpContext, cancellationToken).ConfigureAwait(false);
        returnUrl = returnUrl.NullIfEmpty() ?? Links.AutoClose("Authentication state update").Value;
        return Redirect(returnUrl);
    }
}
