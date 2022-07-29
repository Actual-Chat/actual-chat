using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.Server.Authentication;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authentication.OAuth;
using NetBox.Extensions;

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

    [HttpGet("signInGoogleWithCode/{sessionId}/{code}")]
    public async Task<IActionResult> SignInGoogleWithCode(string sessionId, string code, CancellationToken cancellationToken)
    {
        code = WebUtility.UrlDecode(code);

        var schemeName = GoogleDefaults.AuthenticationScheme;
        var options = _services.GetRequiredService<IOptionsSnapshot<GoogleOptions>>()
            .Get(schemeName);

        using var tokens = await ExchangeCodeAsync(code, options, cancellationToken).ConfigureAwait(false);
        if (tokens.Error != null)
            return BadRequest(tokens.Error);
        if (string.IsNullOrEmpty(tokens.AccessToken))
            return BadRequest("Failed to retrieve access token.");

        // Get the Google user
        var request = new HttpRequestMessage(HttpMethod.Get, options.UserInformationEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var userResponse = await options.Backchannel.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!userResponse.IsSuccessStatusCode)
            return BadRequest($"An error occurred when retrieving Google user information ({userResponse.StatusCode}). Please check if the authentication information is correct.");

        // Build a principal from the Google user
        var claimsIssuer = options.ClaimsIssuer ?? schemeName;
        var identity = new ClaimsIdentity(claimsIssuer);
        var json = await userResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using (var payload = JsonDocument.Parse(json)) {
            var userData = payload.RootElement;
            foreach (var action in options.ClaimActions)
                action.Run(userData, identity, claimsIssuer);
        }
        var principal = new ClaimsPrincipal(identity);

        // Authenticate session with the principal
        var oldUser = HttpContext.User;
        HttpContext.User = principal;
        try {
            var helper = _services.GetRequiredService<ServerAuthHelper>();
            await helper.UpdateAuthState(
                    new Session(sessionId),
                    HttpContext,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally {
            HttpContext.User = oldUser;
        }

        return this.Ok();
    }

    // <summary>
    // Exchanges the authorization code for a authorization token from the remote provider.
    // Implementation is a copy from Microsoft.AspNetCore.Authentication.OAuth.OAuthHandler with small modifications.
    // </summary>
    private async Task<OAuthTokenResponse> ExchangeCodeAsync(string code, OAuthOptions options, CancellationToken requestAborted)
    {
        var tokenRequestParameters = new Dictionary<string, string>(StringComparer.Ordinal) {
            { "client_id", options.ClientId },
            { "redirect_uri", ""  /* context.RedirectUri */ },
            { "client_secret", options.ClientSecret },
            { "code", code },
            { "grant_type", "authorization_code" },
        };

        // We do not use PKCE here, TODO(DF): read more about PKCE.
        // // PKCE https://tools.ietf.org/html/rfc7636#section-4.5, see BuildChallengeUrl
        // if (context.Properties.Items.TryGetValue(OAuthConstants.CodeVerifierKey, out var codeVerifier))
        // {
        //     tokenRequestParameters.Add(OAuthConstants.CodeVerifierKey, codeVerifier!);
        //     context.Properties.Items.Remove(OAuthConstants.CodeVerifierKey);
        // }

        var backChannel = options.Backchannel;

        var requestContent = new FormUrlEncodedContent(tokenRequestParameters!);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, options.TokenEndpoint);
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        requestMessage.Content = requestContent;
        requestMessage.Version = backChannel.DefaultRequestVersion;
        var response = await backChannel.SendAsync(requestMessage, requestAborted).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) {
            var json = await response.Content.ReadAsStringAsync(requestAborted).ConfigureAwait(false);
            var payload = JsonDocument.Parse(json);
            return OAuthTokenResponse.Success(payload);
        }
        else {
            var error = "OAuth token endpoint failure: " + await Display(response).ConfigureAwait(false);
            return OAuthTokenResponse.Failed(new Exception(error));
        }
    }

    [HttpGet("signInGoogleWithIdToken/{sessionId}/{idToken}")]
    public async Task<IActionResult> SignInGoogleWithIdToken(string sessionId, string idToken, CancellationToken cancellationToken)
    {
        idToken = WebUtility.UrlDecode(idToken);

        var options = _services.GetRequiredService<IOptionsSnapshot<GoogleOptions>>()
            .Get(GoogleDefaults.AuthenticationScheme);
        var clientId = options.ClientId;

        try {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken,
                    new GoogleJsonWebSignature.ValidationSettings {
                        Audience = new[] {clientId}
                    })
                .ConfigureAwait(false);
            var json = payload.JsonSerialise();

            throw new NotImplementedException("Create a principal");
            return Content(json);
        }
        catch (Exception e) {
            return BadRequest(e.ToString());
        }
    }

    private static async Task<string> Display(HttpResponseMessage response)
    {
        var output = new StringBuilder();
        output.Append("Status: " + response.StatusCode + ";");
        output.Append("Headers: " + response.Headers.ToString() + ";");
        output.Append("Body: " + await response.Content.ReadAsStringAsync() + ";");
        return output.ToString();
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
