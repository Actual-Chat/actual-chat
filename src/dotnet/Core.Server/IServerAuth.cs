using Microsoft.AspNetCore.Http;

namespace ActualChat;

public interface IServerAuth
{
    Task UpdateAuthState(Session session, HttpContext httpContext, bool assumeAllowed, CancellationToken cancellationToken);
}
