using ActualChat.Web;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server.Authentication;

namespace ActualChat.Users.Controllers;

// NOTE(AY): All requests to this controller must be opened in browser to rather than called via RestEase!
[Route(Route)]
public sealed class MauiAuthController : Controller
{
    public const string Route = "/maui-auth";

    private ServerAuthHelper? _serverAuthHelper;
    private UrlMapper? _urlMapper;
    private ILogger? _log;

    private IServiceProvider Services { get; }
    private ServerAuthHelper ServerAuthHelper => _serverAuthHelper ??= Services.GetRequiredService<ServerAuthHelper>();
    private UrlMapper UrlMapper => _urlMapper ??= Services.GetRequiredService<UrlMapper>();
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public MauiAuthController(IServiceProvider services)
        => Services = services;

    [HttpGet("sign-in/{scheme}")]
    public ActionResult SignIn(string scheme, string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        var session = SessionCookies.Read(HttpContext, "s").RequireValid();
        SessionCookies.Write(HttpContext, session);
        var completeUrl = UrlMapper.ToAbsolute(returnUrl.IsNullOrEmpty()
            ? Links.AutoClose("Sign-in").Value
            : $"{Route}/update-state?returnUrl={returnUrl.UrlEncode()}");
        return Redirect($"/signIn/{scheme}?returnUrl={completeUrl.UrlEncode()}");
    }

    [HttpGet("sign-out")]
    public ActionResult SignOut(string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        var session = SessionCookies.Read(HttpContext, "s").RequireValid();
        SessionCookies.Write(HttpContext, session);
        var completeUrl = UrlMapper.ToAbsolute(returnUrl.IsNullOrEmpty()
            ? Links.AutoClose("Sign-out").Value
            : $"{Route}/update-state?returnUrl={returnUrl.UrlEncode()}");
        return Redirect($"/signOut?returnUrl={completeUrl.UrlEncode()}");
    }

    [HttpGet("update-state")]
    public async Task<ActionResult> UpdateState(string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        var session = SessionCookies.Read(HttpContext).RequireValid();
        await ServerAuthHelper.UpdateAuthState(session, HttpContext, cancellationToken).ConfigureAwait(false);
        returnUrl = returnUrl.NullIfEmpty() ?? Links.AutoClose("Authentication state update").Value;
        return Redirect(returnUrl);
    }
}
