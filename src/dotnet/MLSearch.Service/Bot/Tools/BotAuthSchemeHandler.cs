using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

public class BotAuthSchemeHandler : AuthenticationHandler<BotAuthenticationSchemeOptions>
{
    // This is a workaround implementation.
    private readonly SigningCredentials StubSigningCredentials;
    
    public BotAuthSchemeHandler(
        IOptionsMonitor<BotAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder)
    {
        StubSigningCredentials = options.CurrentValue.SigningCredentials;
    }

    protected async override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var principal = GetValidatedClaims(this.Context.Request);
        if (principal == null) {
            return AuthenticateResult.Fail("Authentication failed");
        }
        var ticket = new AuthenticationTicket(principal, this.Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
    public ClaimsPrincipal GetValidatedClaims(HttpRequest request) {
        const string BEARER_PREFIX = "Bearer ";
        // TODO: Read the token from request headers/cookies
        // Check that it's a valid session, depending on your implementation
        string bearerToken = request.Headers.Authorization.Where(e=> e.StartsWith(BEARER_PREFIX)).FirstOrDefault();
        if (bearerToken == null) {
            return null;
        }
        var claims = VerifyToken(bearerToken.Substring(BEARER_PREFIX.Length));
        if (claims == null){
            return null;
        }
        // If the session is valid, return success:
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims.Claims, "Tokens"));
        return principal;
    }

    public ClaimsPrincipal? VerifyToken(string token)
    {
        var validationParameters = new TokenValidationParameters()
        {
            ValidAudience = "bot-tools.actual.chat",
            ValidIssuer = "integrations.actual.chat",
            ValidateLifetime = true,
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateIssuerSigningKey = true,
            // NOTE: This must be used with a normal Authentication mechanism.
            //IssuerSigningKey = this.Options.SigningCredentials.Key,
            IssuerSigningKey = this.StubSigningCredentials.Key,
            ClockSkew = TimeSpan.Zero
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        try
        {
            SecurityToken validatedToken = null;
            var claims = tokenHandler.ValidateToken(token, validationParameters, out validatedToken);
            if (validatedToken == null) {
                return null;
            }
            return claims;
        }
        catch(SecurityTokenException)
        {
            return null; 
        }
    }
}