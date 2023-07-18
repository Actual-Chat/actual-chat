namespace ActualChat.Security;

public interface ISecureTokensBackend
{
    ValueTask<SecureToken> Create(string value, CancellationToken cancellationToken = default);
    SecureValue? TryParse(string token);
}
