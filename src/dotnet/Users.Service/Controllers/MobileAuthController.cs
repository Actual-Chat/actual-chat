using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.Server.Authentication;

namespace ActualChat.Users.Controllers;

[Route("mobileAuth")]
[ApiController]
public class MobileAuthController : Controller
{
    private const string CallbackScheme = "xamarinessentials";

    private readonly IServiceProvider _services;
    private readonly IAuth _auth;
    private readonly ICommander _commander;

    public MobileAuthController(IServiceProvider services, IAuth auth, ICommander commander)
    {
        _services = services;
        _auth = auth;
        _commander = commander;
    }

    // Example is taken from https://github.com/dotnet/maui/blob/main/src/Essentials/samples/Sample.Server.WebAuthenticator/Controllers/MobileAuthController.cs
    [HttpGet("{scheme}")]
    public async Task Get([FromRoute] string scheme)
    {
        var auth = await Request.HttpContext.AuthenticateAsync(scheme).ConfigureAwait(false);

        if (!auth.Succeeded
            || auth.Principal == null
            || !auth.Principal.Identities.Any(id => id.IsAuthenticated)
            || string.IsNullOrEmpty(auth.Properties.GetTokenValue("access_token"))) {
            // Not authenticated, challenge
            await Request.HttpContext.ChallengeAsync(scheme).ConfigureAwait(false);
        }
        else {
            var claims = auth.Principal.Identities.FirstOrDefault()?.Claims;
            var email = claims?.FirstOrDefault(c => string.Equals(c.Type, ClaimTypes.Email, StringComparison.Ordinal))?.Value;

            // Get parameters to send back to the callback
            (string Key, string? Value)[] qs = {
                ("access_token", auth.Properties.GetTokenValue("access_token")),
                ("refresh_token", auth.Properties.GetTokenValue("refresh_token")),
                ("expires_in", auth.Properties.ExpiresUtc?.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
                ("email", email),
            };

            // Build the result url
            var url = CallbackScheme + "://#" + string.Join(
                "&",
                qs.Where(kvp => !string.IsNullOrEmpty(kvp.Value))
                .Select(kvp => $"{WebUtility.UrlEncode(kvp.Key)}={WebUtility.UrlEncode(kvp.Value)}"));

            // Redirect to final url
            Request.HttpContext.Response.Redirect(url);
        }
    }

    [HttpGet("signIn/{sessionId}/{scheme}")]
    public async Task SignIn(string sessionId, string scheme, CancellationToken cancellationToken)
    {
        if (!HttpContext.User.Identities.Any(id => id.IsAuthenticated)) // Not authenticated, challenge
            await Request.HttpContext.ChallengeAsync(scheme).ConfigureAwait(false);
        else {
            var helper = _services.GetRequiredService<ServerAuthHelper>();
            await helper.UpdateAuthState(
                new Session(sessionId),
                HttpContext,
                cancellationToken).ConfigureAwait(false);

            await WriteAutoClosingMessage(cancellationToken).ConfigureAwait(false);
        }
    }

    [HttpGet("signOut/{sessionId}")]
    public async Task SignOut(string sessionId, CancellationToken cancellationToken)
    {
        var session = new Session(sessionId);
        // Ideally updatePresence should be done once important things are completed
        await using var _ = AsyncDisposable.New(() => _auth.UpdatePresence(session, cancellationToken).ToValueTask()).ConfigureAwait(false);
        await _commander.Call(new SignOutCommand(session), cancellationToken).ConfigureAwait(false);

        await WriteAutoClosingMessage(cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteAutoClosingMessage(CancellationToken cancellationToken)
    {
        string responseString =
            "<html><head></head><body>We are done, please, return to the app.<script>setTimeout(function() { window.close(); }, 1000)</script></body></html>";
        HttpContext.Response.ContentType = "text/html; charset=utf-8";
        await HttpContext.Response.WriteAsync(responseString, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
