using ActualChat.Security;

namespace ActualChat.Users;

public class SecureTokens : ISecureTokens
{
    private ISecureTokensBackend Backend { get; }

    public SecureTokens(IServiceProvider services)
        => Backend = services.GetRequiredService<ISecureTokensBackend>();

    public async Task<SecureToken> Create(string value, CancellationToken cancellationToken = default)
        => await Backend.Create(value, cancellationToken).ConfigureAwait(false);

    public Task<SecureToken> CreateForSession(Session session, CancellationToken cancellationToken = default)
        => Create(session.Id, cancellationToken);
}
