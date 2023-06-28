using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using ActualChat.Users.Module;
using AspNet.Security.OAuth.Apple;
using Cysharp.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Stl.Fusion.Server.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;

namespace ActualChat.Users.Controllers;

[Route("mobileAuth")]
[ApiController]
public sealed class MobileAuthController : Controller
{
    private const string CallbackScheme = "xamarinessentials";

    private IServiceProvider Services { get; }
    private ICommander Commander { get; }
    private ILogger<MobileAuthController> Logger { get; }

    public MobileAuthController(IServiceProvider services)
    {
        Logger = services.GetRequiredService<ILogger<MobileAuthController>>();
        Services = services;
        Commander = services.Commander();
    }

    [Obsolete("Kept only for compatibility with the old API", true)]
    [HttpGet("setupSession/{sessionId}")]
    public async Task<ActionResult> SetupSession(string sessionId, CancellationToken cancellationToken)
    {
        var httpContext = HttpContext;
        var ipAddress = httpContext.GetRemoteIPAddress()?.ToString() ?? "";
        var userAgent = httpContext.Request.Headers.TryGetValue("User-Agent", out var userAgentValues)
            ? userAgentValues.FirstOrDefault() ?? ""
            : "";

        var session = new Session(sessionId);

        var auth = Services.GetRequiredService<IAuth>();
        var sessionInfo = await auth.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        if (sessionInfo?.GetGuestId().IsGuest != true) {
            var setupSessionCommand = new AuthBackend_SetupSession(session, ipAddress, userAgent);
            var commander = Services.Commander();
            await commander.Call(setupSessionCommand, true, cancellationToken).ConfigureAwait(false);
        }
        sessionInfo = await auth.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);

        using var sb = ZString.CreateStringBuilder();
        sb.Append("SessionInfo: ");
        sb.Append(sessionInfo != null ? sessionInfo.SessionHash : "no-session-info");
        sb.AppendLine();
        sb.Append("Account: ");
        try {
            var accounts = Services.GetRequiredService<IAccounts>();
            var account = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
            sb.Append(account.Id.Value);
        }
        catch (Exception e) {
            sb.Append(e);
        }
        return Content(sb.ToString());
    }

    [Obsolete("Kept only for compatibility with the old API", true)]
    [HttpGet("getSession")]
    public Task<ActionResult> GetSession(CancellationToken cancellationToken)
        => GetOrCreateSession(null, cancellationToken);

    [HttpGet("getOrCreateSession/{sid?}")]
    public async Task<ActionResult> GetOrCreateSession(string? sid, CancellationToken cancellationToken)
    {
        var httpContext = HttpContext;
        var sessionResolver = httpContext.RequestServices.GetRequiredService<ISessionResolver>();

        var ipAddress = httpContext.GetRemoteIPAddress()?.ToString() ?? "";
        var userAgent = httpContext.Request.Headers.TryGetValue("User-Agent", out var userAgentValues)
            ? userAgentValues.FirstOrDefault() ?? ""
            : "";

        var auth = Services.GetRequiredService<IAuth>();

        Session? session = null;
        if (!sid.IsNullOrEmpty()
            && await auth.GetSessionInfo(new Session(sid), cancellationToken).ConfigureAwait(false) != null)
            session = new Session(sid);
        session ??= sessionResolver.Session;

        var sessionInfo = await auth.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        if (sessionInfo?.GetGuestId().IsGuest != true) {
            var setupSessionCommand = new AuthBackend_SetupSession(session, ipAddress, userAgent);
            var commander = Services.Commander();
            await commander.Call(setupSessionCommand, true, cancellationToken).ConfigureAwait(false);
        }

        return Content(session.Id.Value);
    }

    // Example is taken from https://github.com/dotnet/maui/blob/main/src/Essentials/samples/Sample.Server.WebAuthenticator/Controllers/MobileAuthController.cs
    [HttpGet("{scheme}")]
    public async Task Get([FromRoute] string scheme)
    {
        var auth = await Request.HttpContext.AuthenticateAsync(scheme).ConfigureAwait(false);

        if (!auth.Succeeded
            || auth.Principal == null
            || !auth.Principal.Identities.Any(id => id.IsAuthenticated)
            || auth.Properties.GetTokenValue("access_token").IsNullOrEmpty()) {
            // Not authenticated, challenge
            await Request.HttpContext.ChallengeAsync(scheme).ConfigureAwait(false);
        }
        else {
            var claims = auth.Principal.Identities.FirstOrDefault()?.Claims;
            var email = claims?.FirstOrDefault(c => OrdinalEquals(c.Type, ClaimTypes.Email))?.Value;

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
                qs.Where(kv => !kv.Value.IsNullOrEmpty())
                .Select(kv => $"{WebUtility.UrlEncode(kv.Key)}={WebUtility.UrlEncode(kv.Value)}"));

            // Redirect to final url
            Request.HttpContext.Response.Redirect(url);
        }
    }

    [HttpGet("signIn/{sessionId}/{scheme}")]
    public async Task SignIn(string sessionId, string scheme, CancellationToken cancellationToken)
    {
        // TODO(DF): Check why sign in works with empty scheme.
        if (!HttpContext.User.Identities.Any(id => id.IsAuthenticated)) // Not authenticated, challenge
            await Request.HttpContext.ChallengeAsync(scheme).ConfigureAwait(false);
        else {
            var helper = Services.GetRequiredService<ServerAuthHelper>();
            await helper.UpdateAuthState(
                new Session(sessionId),
                HttpContext,
                cancellationToken).ConfigureAwait(false);

            await WriteAutoClosingMessage(cancellationToken).ConfigureAwait(false);
        }
    }

    [HttpPost("signInAppleWithCode")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> SignInAppleWithCode(
        [FromForm] IFormCollection request,
        CancellationToken cancellationToken)
    {
        var userId = request["UserId"].ToString();
        if (userId.IsNullOrEmpty())
            throw StandardError.Constraint(nameof(userId), "null or empty");

        var code = request["Code"].ToString();
        if (code.IsNullOrEmpty())
            throw StandardError.Constraint(nameof(code), "null or empty");

        var sessionId = request["SessionId"].ToString();
        if (sessionId.IsNullOrEmpty())
            throw StandardError.Constraint(nameof(sessionId), "null or empty");

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

        using var tokenResponse = await ExchangeCodeAsync(code, options, cancellationToken).ConfigureAwait(false);
        if (tokenResponse.Error != null) {
            Logger.LogError(tokenResponse.Error, $"{nameof(SignInAppleWithCode)} error.");

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
        await AuthenticateSessionWithPrincipal(sessionId, principal, cancellationToken).ConfigureAwait(false);

        return Ok();
    }

    [HttpGet("signInGoogleWithCode/{sessionId}/{code}")]
    public async Task<IActionResult> SignInGoogleWithCode(string sessionId, string code, CancellationToken cancellationToken)
    {
        // https://developers.google.com/identity/protocols/oauth2
        code = WebUtility.UrlDecode(code);

        var schemeName = GoogleDefaults.AuthenticationScheme;
        var options = Services.GetRequiredService<IOptionsSnapshot<GoogleOptions>>()
            .Get(schemeName);

        using var tokens = await ExchangeCodeAsync(code, options, cancellationToken).ConfigureAwait(false);
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

        await AuthenticateSessionWithPrincipal(sessionId, principal, cancellationToken).ConfigureAwait(false);

        return Ok();
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

        var error = "OAuth token endpoint failure: " + await Display(response).ConfigureAwait(false);
        return OAuthTokenResponse.Failed(new Exception(error));
    }

    private static async Task<string> Display(HttpResponseMessage response)
    {
        var output = new StringBuilder();
        output.Append("Status: " + response.StatusCode + ";");
        output.Append("Headers: " + response.Headers + ";");
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        output.Append("Body: " + body + ";");
        return output.ToString();
    }

    [HttpGet("signOut/{sessionId}")]
    public async Task SignOut(string sessionId, CancellationToken cancellationToken)
    {
        var session = new Session(sessionId);
        await Commander.Call(new Auth_SignOut(session), cancellationToken).ConfigureAwait(false);
        await WriteAutoClosingMessage(cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteAutoClosingMessage(CancellationToken cancellationToken)
    {
        string responseString =
            "<html><head></head><body>We are done, please, return to the app.<script>setTimeout(function() { window.close(); }, 1000)</script></body></html>";
        HttpContext.Response.ContentType = "text/html; charset=utf-8";
        await HttpContext.Response.WriteAsync(responseString, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task AuthenticateSessionWithPrincipal(
        string sessionId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var oldUser = HttpContext.User;
        HttpContext.User = principal;
        try {
            var helper = Services.GetRequiredService<ServerAuthHelper>();
            await helper.UpdateAuthState(new Session(sessionId), HttpContext, cancellationToken).ConfigureAwait(false);
        }
        finally {
            HttpContext.User = oldUser;
        }
    }
}
