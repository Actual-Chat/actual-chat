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

public class PhoneAuthHandler : RemoteAuthenticationHandler<PhoneAuthOptions>
{
    private IAuth Auth { get; }
    private UrlMapper UrlMapper { get; }

    public PhoneAuthHandler(
        IOptionsMonitor<PhoneAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        IAuth auth,
        UrlMapper urlMapper) : base(options, logger, encoder, clock)
    {
        Auth = auth;
        UrlMapper = urlMapper;
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var session = Context.GetSession();
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
        var session = Context.GetSession();
        var user = await Auth.GetUser(session, Context.RequestAborted).ConfigureAwait(false);
        if (user?.IsAuthenticated() != true)
            return HandleRequestResult.NoResult();

        var claims = user.Claims.Select(x => new Claim(x.Key, x.Value)).ToList();
        var phoneIdentity = user.Identities.Single(x => OrdinalEquals(x.Key.Schema, Constants.Auth.Phone.SchemeName));
        claims.Add(new Claim(ClaimTypes.NameIdentifier, phoneIdentity.Key.SchemaBoundId));
        var authenticationType = Options.ClaimsIssuer.NullIfEmpty() ?? Constants.Auth.Phone.SchemeName;
        var identity = new ClaimsIdentity(claims, authenticationType);
        return HandleRequestResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity),
            Constants.Auth.Phone.SchemeName));
    }
}
