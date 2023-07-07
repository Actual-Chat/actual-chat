namespace ActualChat.Users;

#pragma warning disable CS0618

public interface IMobileAuth : IMobileSessions
{
    Task<Session> CreateSession(CancellationToken cancellationToken);
    Task<Session> ValidateSession(Session session, CancellationToken cancellationToken);
}
