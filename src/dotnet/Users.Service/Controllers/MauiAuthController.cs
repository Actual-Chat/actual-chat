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
        await ServerAuth.UpdateAuthState(session, HttpContext, true, cancellationToken).ConfigureAwait(false);
        returnUrl = returnUrl.NullIfEmpty() ?? Links.AutoClose("Authentication state update").Value;
        return Redirect(returnUrl);
    }
}
