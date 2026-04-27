namespace ActualChat.Users;

/// <summary>
/// Service for mobile app session creation and validation.
/// </summary>
#pragma warning disable CS0618

public interface IMobileSessions : IComputeService
{
    Task<Session> CreateSession(string appUserAgent, CancellationToken cancellationToken);
    Task<Session> ValidateSession(Session session, string appUserAgent, CancellationToken cancellationToken);

    [Obsolete("2026.03: Use CreateSession(appVersion, ...) instead.")]
    Task<Session> CreateSession(CancellationToken cancellationToken);
    [Obsolete("2026.03: Use ValidateSession(session, appVersion, ...) instead.")]
    Task<Session> ValidateSession(Session session, CancellationToken cancellationToken);
}
