using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ActualChat.MLSearch.Bot.Tools.Context;

public class BotToolsContextHandlerOptions
{
    public SigningCredentials? SigningCredentials { get; set; }
    public string Audience { get; set; } = "";
    public string Issuer { get; set; } = "";
    public TimeSpan ContextLifetime { get; set; }
}

public class BotToolsContext(ClaimsPrincipal? claims) : IBotToolsContext
{
    public const string ConversationClaimType = "ConversationId";
    public string? ConversationId => claims?.FindFirstValue(ConversationClaimType);
    public bool IsValid => claims != null
        && claims.HasClaim(e => string.Equals(e.Type, ConversationClaimType, StringComparison.Ordinal));

}

public class BotToolsContextHandler(IOptionsMonitor<BotToolsContextHandlerOptions> options) : IBotToolsContextHandler
{
    private const string BearerPrefix = "Bearer ";

    public IBotToolsContext GetContext(HttpRequest request) => new BotToolsContext(GetValidatedClaims(request));
    public void SetContext(HttpRequestMessage request, string conversationId)
    {
        var config = options.CurrentValue;
        var claims = new List<Claim>();
        claims.Add(new Claim(BotToolsContext.ConversationClaimType, conversationId));
        var expiration = DateTime.UtcNow.Add(config.ContextLifetime);

        var token = new JwtSecurityToken(
            issuer: config.Issuer,
            audience: config.Audience,
            claims: claims,
            expires: expiration,
            signingCredentials: config.SigningCredentials
        );

        var signedToken = new JwtSecurityTokenHandler().WriteToken(token);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signedToken);
    }

    private ClaimsPrincipal? GetValidatedClaims(HttpRequest request)
    {
        string? bearerToken = request.Headers.Authorization
            .FirstOrDefault(e => e?.StartsWith(BearerPrefix, StringComparison.Ordinal) == true);
        if (bearerToken == null) {
            return null;
        }
        var claims = VerifyToken(bearerToken.Substring(BearerPrefix.Length));
        if (claims == null) {
            return null;
        }
        // If the session is valid, return success:
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims.Claims, nameof(BotToolsContext)));
        return principal;
    }

    private ClaimsPrincipal? VerifyToken(string signedToken)
    {
        var config = options.CurrentValue;
        var signingCredentials = config.SigningCredentials.Require();
        var validationParameters = new TokenValidationParameters() {
            ValidAudience = config.Audience,
            ValidIssuer = config.Issuer,
            ValidateLifetime = true,
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingCredentials?.Key,
            ClockSkew = TimeSpan.Zero
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        try {
            var claims = tokenHandler.ValidateToken(signedToken, validationParameters, out var validatedToken);
            if (validatedToken == null) {
                return null;
            }
            return claims;
        }
        catch (SecurityTokenException) {
            return null;
        }
    }
}
