using ActualLab.Rpc;

namespace ActualChat.Security;

/// <summary>
/// Service for creating time-limited secure tokens.
/// </summary>
public interface ISecureTokens : IRpcService
{
    Task<SecureToken> Create(string value, CancellationToken cancellationToken = default);
    // The overload with Session is needed to handle default Session
    Task<SecureToken> CreateForSession(Session session, CancellationToken cancellationToken = default);
}
