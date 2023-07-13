using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server.Authentication;

namespace ActualChat.Users.Controllers;

[Route("mobileAuthV2")]
[ApiController]
public sealed class MobileAuthV2Controller : Controller
{
    private IServiceProvider Services { get; }
    private ICommander Commander { get; }
    private ILogger Log { get; }

    public MobileAuthV2Controller(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Commander = services.Commander();
    }

    [HttpGet("signIn/{scheme}/{sessionId}")]
    public async Task<ActionResult> SignIn(
        string scheme, string sessionId, string returnUrl,
        CancellationToken cancellationToken)
    {
        var session = new Session(sessionId).RequireValid();
        if (!HttpContext.User.Identities.Any(id => id.IsAuthenticated)) // Not authenticated, challenge
            await Request.HttpContext.ChallengeAsync(scheme).ConfigureAwait(false);

        var serverAuthHelper = Services.GetRequiredService<ServerAuthHelper>();
        await serverAuthHelper.UpdateAuthState(session, HttpContext, cancellationToken).ConfigureAwait(false);
        return Redirect(returnUrl);
    }

    [HttpGet("signOut/{sessionId}")]
    public async Task<ActionResult> SignOut(string sessionId, string returnUrl, CancellationToken cancellationToken)
    {
        var session = new Session(sessionId).RequireValid();
        _ = Commander.Run(new Auth_SignOut(session), cancellationToken).ConfigureAwait(false);
        if (HttpContext.User.Identities.Any(id => id.IsAuthenticated)) // Not authenticated, challenge
            await Request.HttpContext.SignOutAsync().ConfigureAwait(false);

        return Redirect(returnUrl);
    }
}
