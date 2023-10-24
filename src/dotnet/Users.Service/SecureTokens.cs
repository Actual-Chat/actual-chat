using ActualChat.Security;

namespace ActualChat.Users;

public class SecureTokens(IServiceProvider services) : ISecureTokens
{
    private ISecureTokensBackend Backend { get; } = services.GetRequiredService<ISecureTokensBackend>();

    public async Task<SecureToken> Create(string value, CancellationToken cancellationToken = default)
        => await Backend.Create(value, cancellationToken).ConfigureAwait(false);

    public async Task<SecureToken> CreateForSession(Session session, CancellationToken cancellationToken = default)
        => await Create(session.RequireValid().Id, cancellationToken).ConfigureAwait(false);
}
