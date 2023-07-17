using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Stl.Generators;

namespace ActualChat.Users;

public class AuthTokensBackend : IAuthTokensBackend
{
    private ITimeLimitedDataProtector DataProtector { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public AuthTokensBackend(IServiceProvider services)
    {
        DataProtector = services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(nameof(MobileSessions))
            .ToTimeLimitedDataProtector();
        Clocks = services.Clocks();
        Log = services.LogFor(GetType());
    }

    // Not a [ComputeMethod]!
    public Task<AuthToken> Create(Session session, TokenType tokenType, CancellationToken cancellationToken)
    {
        session.RequireValid();
        var expires = Clocks.CoarseSystemClock.UtcNow.AddMinutes(15);
        var secret = $"{session.Id}.{RandomStringGenerator.Default.Next(8)}.{(int)tokenType}";
        var tokenValue = DataProtector.Protect(secret, expires);
        return Task.FromResult(new AuthToken(tokenValue, expires));
    }

    // Not a [ComputeMethod]!
    public Task<Session> Validate(string token, TokenType tokenType, CancellationToken cancellationToken)
    {
        try {
            var secret = DataProtector.Unprotect(token, out var expiresAt);
            var splitSecret = secret.Split('.');
            if (splitSecret.Length != 3)
                throw StandardError.Unauthorized("Invalid token format");

            if (Clocks.CoarseSystemClock.UtcNow > expiresAt)
                throw StandardError.Unauthorized("Token has expired");

            var sessionId = splitSecret[0];
            var tokenTypeString = splitSecret[2];
            if (!int.TryParse(tokenTypeString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var storedTokenType))
                throw StandardError.Unauthorized("Invalid token type value");

            if (storedTokenType != (int)tokenType)
                throw StandardError.Unauthorized("Requested token type doesn't match with provided token");

            var session = new Session(sessionId).RequireValid();
            return Task.FromResult(session.RequireValid());
        }
        catch (CryptographicException e) {
            Log.LogError(e, "Invalid AuthToken");
            throw StandardError.Unauthorized("Your are not authorized to perform action TokenType=" + tokenType);
        }
    }
}
