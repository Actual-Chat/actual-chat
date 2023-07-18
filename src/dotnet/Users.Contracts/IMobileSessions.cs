namespace ActualChat.Users;

#pragma warning disable CS0618

public interface IMobileSessions : IMobileAuth
{ }

[Obsolete("Retired in favour of IMobileSessions.")]
public interface IMobileAuth : IMobileSessionsV1
{
    Task<Session> CreateSession(CancellationToken cancellationToken);
    Task<Session> ValidateSession(Session session, CancellationToken cancellationToken);
}

[Obsolete("Retired in favour of IMobileSessions.")]
public interface IMobileSessionsV1 : IComputeService
{
    [Obsolete("2023.07: Retired in favour of IMobileAuth.")]
    Task<string> Create(CancellationToken cancellationToken);
    [Obsolete("2023.07: Retired in favour of IMobileAuth.")]
    Task<string> Validate(string sessionId, CancellationToken cancellationToken);
}
