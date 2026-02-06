namespace ActualChat.Users;

/// <summary>
/// Service for mobile app session creation and validation.
/// </summary>
#pragma warning disable CS0618

public interface IMobileSessions : IComputeService
{
    Task<Session> CreateSession(CancellationToken cancellationToken);
    Task<Session> ValidateSession(Session session, CancellationToken cancellationToken);
}
