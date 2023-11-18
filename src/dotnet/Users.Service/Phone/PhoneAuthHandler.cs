using System.Security.Claims;
using System.Text.Encodings.Web;
using ActualChat.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ActualChat.Users;

public class PhoneAuthOptions : RemoteAuthenticationOptions
{
    public string LoginUrl { get; set; } = "/login/phone";
}

#pragma warning disable CS0618 // Type or member is obsolete (ISystemClock)
public class PhoneAuthHandler(
        IOptionsMonitor<PhoneAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        IAuth auth,
        UrlMapper urlMapper)
    : RemoteAuthenticationHandler<PhoneAuthOptions>(options, logger, encoder, clock)
#pragma warning restore CS0618 // Type or member is obsolete
{
    private IAuth Auth { get; } = auth;
    private UrlMapper UrlMapper { get; } = urlMapper;

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var session = Context.GetSessionFromCookie();
        var authInfo = await Auth.GetSessionInfo(session, CancellationToken.None).ConfigureAwait(false);
        if (authInfo?.IsAuthenticated() == true) {
            Response.Redirect(properties.RedirectUri ?? UrlMapper.BaseUrl);
            return;
        }

        var loginPage = $"{Options.LoginUrl}?callbackPath={UrlEncoder.Encode(Options.CallbackPath)}";
        Response.Redirect(loginPage);
    }

    protected override async Task<HandleRequestResult> HandleRemoteAuthenticateAsync()
    {
        var session = Context.GetSessionFromCookie();
        var user = await Auth.GetUser(session, Context.RequestAborted).ConfigureAwait(false);
        if (user?.IsAuthenticated() != true)
            return HandleRequestResult.NoResult();

        user = user.WithClaim(ClaimTypes.NameIdentifier, user.GetPhoneIdentity().SchemaBoundId);
        var claims = user.Claims.Select(x => new Claim(x.Key, x.Value));
        var authenticationType = Options.ClaimsIssuer.NullIfEmpty() ?? Constants.Auth.Phone.SchemeName;
        var identity = new ClaimsIdentity(claims, authenticationType);
        return HandleRequestResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity),
            Constants.Auth.Phone.SchemeName));
    }
}
