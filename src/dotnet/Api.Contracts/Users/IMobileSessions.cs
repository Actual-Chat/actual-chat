namespace ActualChat.Users;

/// <summary>
/// Service for mobile app session creation and validation.
/// </summary>
#pragma warning disable CS0618

public interface IMobileSessions : IComputeService
{
    Task<Session> CreateSession(string appUserAgent, CancellationToken cancellationToken);
    Task<Session> ValidateSession(Session session, string appUserAgent, CancellationToken cancellationToken);

}
