using ActualLab.Rpc;

namespace ActualChat.Security;

/// <summary>
/// Backend service for creating and parsing secure tokens.
/// </summary>
public interface ISecureTokensBackend : IBackendService
{
    ValueTask<SecureToken> Create(
        SecureTokenKind kind,
        string value,
        CancellationToken cancellationToken = default);
    DecryptedSecureToken? TryDecrypt(SecureTokenKind kind, string token);
}

public enum SecureTokenKind
{
    Session = 0,
    PendingRegistration = 1,
}
