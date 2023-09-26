using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using ActualChat.Users.Module;
using ActualChat.Web;
using AspNet.Security.OAuth.Apple;
using Cysharp.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Stl.Fusion.Server.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

// [JsonifyErrors] use is intended: all endpoints here are invoked from .NET only
[ApiController, Route("api/native-auth"), JsonifyErrors]
public sealed class NativeAuthController(IServiceProvider services) : ControllerBase
{
    private ILogger? _log;

    private IServiceProvider Services { get; } = services;
    private ILogger Log => _log ??= Services.LogFor(GetType());

    [HttpGet("sign-in-apple")]
    public async Task SignInApple(
        string userId,
        string code,
        string? email,
        string? name,
        CancellationToken cancellationToken)
    {
        var session = HttpContext.GetSessionFromHeader();
        userId.RequireNonEmpty();
        code.RequireNonEmpty();

        var authenticationHandlerProvider = Services.GetRequiredService<IAuthenticationHandlerProvider>();
        var userSettings = Services.GetRequiredService<UsersSettings>();
        var handler = await authenticationHandlerProvider
            .GetHandlerAsync(HttpContext, AppleAuthenticationDefaults.AuthenticationScheme)
            .ConfigureAwait(false);
        if (handler is not AppleAuthenticationHandler appleAuthHandler)
            throw StandardError.NotFound<AppleAuthenticationHandler>();

        var options = appleAuthHandler.Options;
        var context = new AppleGenerateClientSecretContext(HttpContext, appleAuthHandler.Scheme, options);
        options.ClientId = userSettings.AppleAppId;
        options.ClientSecret = await options.ClientSecretGenerator.GenerateAsync(context).ConfigureAwait(false);

        using var token = await ExchangeCode(code, options, cancellationToken).ConfigureAwait(false);
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
    }

    [HttpGet("sign-in-google")]
    public async Task SignInGoogle(string code, CancellationToken cancellationToken)
    {
        var session = HttpContext.GetSessionFromHeader();
        // code = code.UrlDecode(); // Weird, but this is somehow necessary
        code.RequireNonEmpty();
        var schemeName = GoogleDefaults.AuthenticationScheme;
        var options = Services
            .GetRequiredService<IOptionsSnapshot<GoogleOptions>>()
            .Get(schemeName);

        using var token = await ExchangeCode(code, options, cancellationToken).ConfigureAwait(false);
        var request = new HttpRequestMessage(HttpMethod.Get, options.UserInformationEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var userResponse = await options.Backchannel.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!userResponse.IsSuccessStatusCode) {
            var message =
                $"An error occurred when retrieving Google user information ({userResponse.StatusCode}). "
                + $"Please check if the authentication information is correct.";
            throw StandardError.External(message);
        }

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
    }

    // Private methods

    // Exchanges the authorization code for a authorization token from the remote provider.
    // Implementation is a copy from Microsoft.AspNetCore.Authentication.OAuth.OAuthHandler with small modifications.
    private async Task<OAuthTokenResponse> ExchangeCode(string code, OAuthOptions options, CancellationToken requestAborted)
    {
        var requestParameters = new Dictionary<string, string>(StringComparer.Ordinal) {
            { "client_id", options.ClientId },
            { "redirect_uri", ""  /* context.RedirectUri */ },
            { "client_secret", options.ClientSecret },
            { "code", code },
            { "grant_type", "authorization_code" },
        };

        // We do not use PKCE here, TODO(DF): read more about PKCE.
        // // PKCE https://tools.ietf.org/html/rfc7636#section-4.5, see BuildChallengeUrl
        // if (context.Properties.Items.TryGetValue(OAuthConstants.CodeVerifierKey, out var codeVerifier)) {
        //     tokenRequestParameters.Add(OAuthConstants.CodeVerifierKey, codeVerifier!);
        //     context.Properties.Items.Remove(OAuthConstants.CodeVerifierKey);
        // }

        var backChannel = options.Backchannel;
        var requestContent = new FormUrlEncodedContent(requestParameters!);
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, options.TokenEndpoint);
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        requestMessage.Content = requestContent;
        requestMessage.Version = backChannel.DefaultRequestVersion;
        var response = await backChannel.SendAsync(requestMessage, requestAborted).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            var message = "OAuth token endpoint failure: " + await Format(response).ConfigureAwait(false);
            throw StandardError.External(message);
        }

        var json = await response.Content.ReadAsStringAsync(requestAborted).ConfigureAwait(false);
        var result = OAuthTokenResponse.Success(JsonDocument.Parse(json));
        if (result.AccessToken.IsNullOrEmpty())
            throw StandardError.External("Failed to retrieve access token.");
        return result;
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

    private static async Task<string> Format(HttpResponseMessage response)
    {
        using var sb = ZString.CreateStringBuilder();
        sb.Append("Status: " + response.StatusCode + "; ");
        sb.Append("Headers: " + response.Headers + "; ");
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        sb.Append("Body: " + body);
        return sb.ToString();
    }
}
