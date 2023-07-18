namespace ActualChat.Users;

public interface ISecureTokensBackend
{
    ValueTask<SecureToken> Create(string value, CancellationToken cancellationToken);
    ValueTask<Expiring<string>?> TryParse(string token, CancellationToken cancellationToken);
}
