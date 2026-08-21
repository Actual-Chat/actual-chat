using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using ActualChat.Users.Module;
using AspNet.Security.OAuth.Apple;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ActualChat.Users;

public class NativeAuth(IServiceProvider services) : INativeAuth
{
    private IServiceProvider Services { get; } = services;
    private UsersSettings Settings { get; } = services.GetRequiredService<UsersSettings>();
    private AuthHelper AuthHelper { get; } = services.GetRequiredService<AuthHelper>();
    private ICommander Commander { get; } = services.Commander();
    private IOptionsFactory<AppleAuthenticationOptions> AppleOptionsFactory { get; }
        = services.GetRequiredService<IOptionsFactory<AppleAuthenticationOptions>>();
    private IOptionsMonitor<GoogleOptions> GoogleOptions { get; }
        = services.GetRequiredService<IOptionsMonitor<GoogleOptions>>();
    private ILogger Log { get; } = services.LogFor<NativeAuth>();

    // [CommandHandler]
    public virtual async Task OnSignInGoogle(NativeAuth_SignInGoogle command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var session = command.Session;
        var code = command.Code;
        try {
            code.RequireNonEmpty();

            var schemeName = GoogleDefaults.AuthenticationScheme;
            var options = GoogleOptions.Get(schemeName);
            using var token = await ExchangeCode(
                    code, options.Backchannel, options.TokenEndpoint, options.ClientId, options.ClientSecret, cancellationToken)
                .ConfigureAwait(false);

            var request = new HttpRequestMessage(HttpMethod.Get, options.UserInformationEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            var userResponse = await options.Backchannel.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!userResponse.IsSuccessStatusCode) {
                var message =
                    $"An error occurred when retrieving Google user information ({userResponse.StatusCode}). "
                    + "Please check if the authentication information is correct.";
                throw StandardError.External(message);
            }

            // Build a principal from the Google user
            var identity = new ClaimsIdentity(AuthSchema.Google);
            var json = await userResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using (var payload = JsonDocument.Parse(json)) {
                var userData = payload.RootElement;
                foreach (var action in options.ClaimActions)
                    action.Run(userData, identity, AuthSchema.Google);
            }

            var principal = new ClaimsPrincipal(identity);
            await SignIn(session, principal, "Unable to sign in with Google.", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            await ReportError(session, "Unable to sign in with Google.", e, cancellationToken).ConfigureAwait(false);
        }
    }

    // [CommandHandler]
    public virtual async Task OnSignInApple(NativeAuth_SignInApple command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var session = command.Session;
        var userId = command.UserId;
        var code = command.Code;
        var name = command.Name;
        try {
            userId.RequireNonEmpty();
            code.RequireNonEmpty();

            var schemeName = AppleAuthenticationDefaults.AuthenticationScheme;
            // Use a fresh, isolated options instance so we can safely override ClientId for the native
            // app id without mutating the shared singleton. The secret generator reads ClientId to build
            // the JWT 'iss'/'sub', and Apple's token endpoint requires it to match the request's client_id.
            var options = AppleOptionsFactory.Create(schemeName);
            options.ClientId = Settings.AppleAppId;

            var stubHttpContext = new DefaultHttpContext { RequestServices = Services };
            var scheme = new AuthenticationScheme(schemeName, schemeName, typeof(AppleAuthenticationHandler));
            var secretContext = new AppleGenerateClientSecretContext(stubHttpContext, scheme, options);
            var clientSecret = await options.ClientSecretGenerator.GenerateAsync(secretContext).ConfigureAwait(false);

            using var token = await ExchangeCode(
                    code, options.Backchannel, options.TokenEndpoint, options.ClientId, clientSecret, cancellationToken)
                .ConfigureAwait(false);

            // Use sub and email from Apple's id_token (received server-to-server),
            // NOT from caller-supplied params which can be spoofed.
            var (idTokenSub, idTokenEmail, isEmailVerified) = ParseAppleIdToken(token, options.ClientId);

            var identity = new ClaimsIdentity(AuthSchema.Apple);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, idTokenSub));
            if (!idTokenEmail.IsNullOrEmpty())
                identity.AddClaim(new Claim(ClaimTypes.Email, idTokenEmail));
            if (isEmailVerified)
                identity.AddClaim(
                    new Claim(AuthSchema.EmailVerifiedClaim, bool.TrueString, ClaimValueTypes.Boolean));
            if (!name.IsNullOrEmpty()) {
                var names = name.Split(' ');
                switch (names.Length) {
                case 1:
                    identity.AddClaim(new Claim(ClaimTypes.GivenName, names[0]));
                    break;
                case > 1:
                    identity.AddClaim(new Claim(ClaimTypes.GivenName, names[0]));
                    identity.AddClaim(new Claim(ClaimTypes.Surname, string.Join(' ', names.Skip(1))));
                    break;
                }
            }

            var principal = new ClaimsPrincipal(identity);
            await SignIn(session, principal, "Unable to sign in with Apple.", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            await ReportError(session, "Unable to sign in with Apple.", e, cancellationToken).ConfigureAwait(false);
        }
    }

    // [CommandHandler]
    public virtual async Task OnSignOut(NativeAuth_SignOut command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var session = command.Session;
        var signOutCommand = new AccountsBackend_SignOut(session);
        await ((Task)Commander.Call(signOutCommand, true, cancellationToken)).ConfigureAwait(false);
    }

    // Private methods

    private async Task SignIn(
        Session session,
        ClaimsPrincipal principal,
        string defaultErrorMessage,
        CancellationToken cancellationToken)
    {
        try {
            await AuthHelper.SignIn(session, principal, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            // Match the legacy controller: surface the underlying error message via SessionTemporals,
            // so ProviderSelectStep can display it. Don't rethrow — the caller (MAUI) doesn't watch the result.
            var signInError = e.Message.NullIfEmpty() ?? defaultErrorMessage;
            var setErrorCmd = new SessionTemporalsBackend_Set(
                session, Constants.SessionTemporals.SignInErrorKey, signInError);
            await Commander.Run(setErrorCmd, true, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReportError(Session session, string defaultMessage, Exception e, CancellationToken cancellationToken)
    {
        Log.LogError(e, "{Message}", defaultMessage);
        var setErrorCmd = new SessionTemporalsBackend_Set(
            session, Constants.SessionTemporals.SignInErrorKey, defaultMessage);
        await Commander.Run(setErrorCmd, true, cancellationToken).ConfigureAwait(false);
    }

    // Exchanges the OAuth authorization code for an access/id token.
    // Adapted from Microsoft.AspNetCore.Authentication.OAuth.OAuthHandler.
    private static async Task<OAuthTokenResponse> ExchangeCode(
        string code,
        HttpClient backchannel,
        string tokenEndpoint,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        var requestParameters = new Dictionary<string, string>() {
            { "client_id", clientId },
            { "redirect_uri", "" },
            { "client_secret", clientSecret },
            { "code", code },
            { "grant_type", "authorization_code" },
        };

        var requestContent = new FormUrlEncodedContent(requestParameters!);
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint);
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        requestMessage.Content = requestContent;
        requestMessage.Version = backchannel.DefaultRequestVersion;
        var response = await backchannel.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            var message = "OAuth token endpoint failure: " + await Format(response).ConfigureAwait(false);
            throw StandardError.External(message);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = OAuthTokenResponse.Success(JsonDocument.Parse(json));
        if (result.AccessToken.IsNullOrEmpty())
            throw StandardError.External("Failed to retrieve access token.");
        return result;
    }

    private static (string Sub, string? Email, bool IsEmailVerified) ParseAppleIdToken(
        OAuthTokenResponse token,
        string clientId)
    {
        var idToken = token.Response!.RootElement.GetProperty("id_token").GetString()
            ?? throw StandardError.External("Apple id_token is missing from token response.");

        var jwt = new JwtSecurityToken(idToken);
        if (jwt.Issuer != AppleAuthenticationConstants.Audience)
            throw StandardError.External("Apple id_token has an invalid issuer.");
        if (!jwt.Audiences.Contains(clientId))
            throw StandardError.External("Apple id_token has an invalid audience.");
        var now = DateTime.UtcNow;
        if (jwt.ValidFrom > now || jwt.ValidTo <= now)
            throw StandardError.External("Apple id_token is outside its valid lifetime.");

        var sub = jwt.Subject
            ?? throw StandardError.External("Apple id_token is missing 'sub' claim.");
        var email = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
        var emailVerifiedClaim = jwt.Claims.FirstOrDefault(c => c.Type == AuthSchema.EmailVerifiedClaim)?.Value;
        return (sub, email, AuthSchema.IsVerifiedEmailClaim(emailVerifiedClaim));
    }

    private static async Task<string> Format(HttpResponseMessage response)
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        sb.Append("Status: " + response.StatusCode + "; ");
        sb.Append("Headers: " + response.Headers + "; ");
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        sb.Append("Body: " + body);
        return sb.ToStringAndRelease();
    }
}
