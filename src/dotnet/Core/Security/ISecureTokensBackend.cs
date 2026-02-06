using ActualLab.Rpc;

namespace ActualChat.Security;

/// <summary>
/// Backend service for creating and parsing secure tokens.
/// </summary>
public interface ISecureTokensBackend : IBackendService
{
    ValueTask<SecureToken> Create(string value, CancellationToken cancellationToken = default);
    SecureValue? TryParse(string token);
}
