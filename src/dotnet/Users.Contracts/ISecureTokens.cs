using Stl.Rpc;

namespace ActualChat.Users;

public interface ISecureTokens: IRpcService
{
    Task<SecureToken> Create(string value, CancellationToken cancellationToken);
    // The overload with Session is needed to handle default Session
    Task<SecureToken> CreateForSession(Session session, CancellationToken cancellationToken);
}
