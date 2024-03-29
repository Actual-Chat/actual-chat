using ActualLab.Rpc;

namespace ActualChat.Security;

public interface ISecureTokensBackend : IBackendService
{
    ValueTask<SecureToken> Create(string value, CancellationToken cancellationToken = default);
    SecureValue? TryParse(string token);
}
