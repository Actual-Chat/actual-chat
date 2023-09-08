using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using ActualChat.Users.Module;
using AspNet.Security.OAuth.Apple;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Stl.Fusion.Server.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;

namespace ActualChat.Users.Controllers;

[Obsolete("2023.07: Kept only for compatibility with the old API.")]
[ApiController, Route("mobileAuth")]
public sealed class LegacyMobileAuthController : Controller
{
    private ICommander? _commander;
    private ILogger? _log;

    private IServiceProvider Services { get; }
    private ICommander Commander => _commander ??= Services.Commander();
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public LegacyMobileAuthController(IServiceProvider services)
        => Services = services;

    [HttpGet("signIn/{sessionId}/{scheme}")]
    public async Task<ActionResult> SignIn(string sessionId, string scheme, CancellationToken cancellationToken)
    {
        var session = new Session(sessionId).RequireValid();
        // TODO(DF): Check why sign in works with empty scheme
        if (!HttpContext.User.Identities.Any(id => id.IsAuthenticated)) // Not authenticated, challenge
            await Request.HttpContext.ChallengeAsync(scheme).ConfigureAwait(false);

        var serverAuthHelper = Services.GetRequiredService<ServerAuthHelper>();
        await serverAuthHelper.UpdateAuthState(session, HttpContext, cancellationToken).ConfigureAwait(false);
        return Redirect(Links.AutoClose("Sign-in"));
    }

    [HttpGet("signOut/{sessionId}")]
    public async Task<ActionResult> SignOut(string sessionId, CancellationToken cancellationToken)
    {
        var session = new Session(sessionId).RequireValid();
        await Commander.Call(new Auth_SignOut(session), cancellationToken).ConfigureAwait(false);
        return Redirect(Links.AutoClose("Sign-out"));
    }

    [HttpPost("signInAppleWithCode")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> SignInAppleWithCode(
        [FromForm] IFormCollection request,
        CancellationToken cancellationToken)
    {
        var session = new Session(request["SessionId"].ToString()).RequireValid();
        var userId = request["UserId"].ToString();
        userId.RequireNonEmpty();
        var code = request["Code"].ToString();
        code.RequireNonEmpty();
        var email = request["Email"].ToString();
        var name = request["Name"].ToString();

        var authenticationHandlerProvider = Services.GetRequiredService<IAuthenticationHandlerProvider>();
        var userSettings = Services.GetRequiredService<UsersSettings>();
        var handler = await authenticationHandlerProvider
            .GetHandlerAsync(HttpContext, AppleAuthenticationDefaults.AuthenticationScheme)
            .ConfigureAwait(false);
        if (handler is not AppleAuthenticationHandler appleAuthHandler)
            throw new InvalidOperationException($"{nameof(AppleAuthenticationHandler)} is not found.");

        var options = appleAuthHandler.Options;
        var context = new AppleGenerateClientSecretContext(HttpContext, appleAuthHandler.Scheme, options);
        options.ClientId = userSettings.AppleAppId;
        options.ClientSecret = await options.ClientSecretGenerator.GenerateAsync(context).ConfigureAwait(false);

        using var tokenResponse = await ExchangeCode(code, options, cancellationToken).ConfigureAwait(false);
        if (tokenResponse.Error != null) {
            Log.LogError(tokenResponse.Error, $"{nameof(SignInAppleWithCode)} error.");
            return BadRequest();
        }

        var identity = new ClaimsIdentity(options.ClaimsIssuer);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        if (!email.IsNullOrEmpty())
            identity.AddClaim(new Claim(ClaimTypes.Email, email));

        if (!name.IsNullOrEmpty()) {
            var names = name.Split(' ');
            switch (names.Length) {
                case 1: {
                    identity.AddClaim(new Claim(ClaimTypes.GivenName, names[0]));
                    break;
                }
                case > 1: {
                    var firstName = names[0];
                    identity.AddClaim(new Claim(ClaimTypes.GivenName, firstName));
                    var lastName = string.Join(' ', names.Skip(1));
                    identity.AddClaim(new Claim(ClaimTypes.Surname, lastName!));
                    break;
                }
            }
        }

        var principal = new ClaimsPrincipal(identity);
        await UpdateAuthStateWithPrincipal(session, principal, cancellationToken).ConfigureAwait(false);
        return Ok();
    }

    [HttpGet("signInGoogleWithCode/{sessionId}/{code}")]
    public async Task<IActionResult> SignInGoogleWithCode(string sessionId, string code, CancellationToken cancellationToken)
    {
        var session = new Session(sessionId).RequireValid();
        code = code.UrlDecode(); // Weird, but this is somehow necessary
        var schemeName = GoogleDefaults.AuthenticationScheme;
        var options = Services
            .GetRequiredService<IOptionsSnapshot<GoogleOptions>>()
            .Get(schemeName);

        using var tokens = await ExchangeCode(code, options, cancellationToken).ConfigureAwait(false);
        if (tokens.Error != null)
            return BadRequest(tokens.Error);
        if (tokens.AccessToken.IsNullOrEmpty())
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

        await UpdateAuthStateWithPrincipal(session, principal, cancellationToken).ConfigureAwait(false);
        return Ok();
    }

    // Private methods

    // Exchanges the authorization code for a authorization token from the remote provider.
    // Implementation is a copy from Microsoft.AspNetCore.Authentication.OAuth.OAuthHandler with small modifications.
    private async Task<OAuthTokenResponse> ExchangeCode(string code, OAuthOptions options, CancellationToken requestAborted)
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

        var message = "OAuth token endpoint failure: " + await Format(response).ConfigureAwait(false);
        return OAuthTokenResponse.Failed(new Exception(message));
    }

    private static async Task<string> Format(HttpResponseMessage response)
    {
        var output = new StringBuilder();
        output.Append("Status: " + response.StatusCode + ";");
        output.Append("Headers: " + response.Headers + ";");
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        output.Append("Body: " + body + ";");
        return output.ToString();
    }

    private async Task UpdateAuthStateWithPrincipal(
        Session session,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var oldUser = HttpContext.User;
        HttpContext.User = principal;
        try {
            var helper = Services.GetRequiredService<ServerAuthHelper>();
            await helper.UpdateAuthState(session, HttpContext, cancellationToken).ConfigureAwait(false);
        }
        finally {
            HttpContext.User = oldUser;
        }
    }
}
