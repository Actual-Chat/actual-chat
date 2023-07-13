using ActualChat.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server.Authentication;

namespace ActualChat.Users.Controllers;

[Route("mobileAuthV2")]
[ApiController]
public sealed class MobileAuthV2Controller : Controller
{
    private ServerAuthHelper? _serverAuthHelper;

    private IServiceProvider Services { get; }
    private ServerAuthHelper ServerAuthHelper => _serverAuthHelper ??= Services.GetRequiredService<ServerAuthHelper>();
    private ICommander Commander { get; }
    private ILogger Log { get; }

    public MobileAuthV2Controller(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Commander = services.Commander();
    }

    [HttpGet("signIn/{scheme}/{sessionId}")]
    public ActionResult SignIn(
        string scheme, string sessionId, string returnUrl,
        CancellationToken cancellationToken)
    {
        SessionCookies.Write(HttpContext, new Session(sessionId));
        var completeUrl = $"/mobileAuthV2/complete?returnUrl={returnUrl.UrlEncode()}";
        return Redirect($"/signIn/{scheme}?returnUrl={completeUrl.UrlEncode()}");
    }

    [HttpGet("signOut/{sessionId}")]
    public ActionResult SignOut(string sessionId, string returnUrl, CancellationToken cancellationToken)
    {
        SessionCookies.Write(HttpContext, new Session(sessionId));
        var completeUrl = $"/mobileAuthV2/complete?returnUrl={returnUrl.UrlEncode()}";
        return Redirect($"/signOut?returnUrl={completeUrl.UrlEncode()}");
    }

    [HttpGet("complete/{sessionId}")]
    public async Task<ActionResult> Complete(string returnUrl, CancellationToken cancellationToken)
    {
        var session = SessionCookies.Read(HttpContext).RequireValid();
        await ServerAuthHelper.UpdateAuthState(session, HttpContext, cancellationToken).ConfigureAwait(false);
        return Redirect(returnUrl);
    }
}
