using Microsoft.AspNetCore.DataProtection;

namespace ActualChat.Users;

public class AuthTokens: IAuthTokens
{
    private ITimeLimitedDataProtector DataProtector { get; }
    private IAuthTokensBackend AuthTokensBackend { get; }
    private ILogger Log { get; }

    public AuthTokens(IServiceProvider services)
    {
        DataProtector = services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(nameof(MobileSessions))
            .ToTimeLimitedDataProtector();
        AuthTokensBackend = services.GetRequiredService<IAuthTokensBackend>();
        Log = services.LogFor(GetType());
    }

    // Not a [ComputeMethod]!
    public Task<AuthToken> Create(Session session, TokenType tokenType, CancellationToken cancellationToken)
        => AuthTokensBackend.Create(session, tokenType, cancellationToken);
}
