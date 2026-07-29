namespace ActualChat.Users;

/// <summary>
/// Extension methods for <see cref="SessionInfoFull"/>.
/// </summary>
public static class SessionInfoFullExt
{
    [return: NotNullIfNotNull(nameof(sessionInfo))]
    public static SessionInfo? ToSessionInfo(this SessionInfoFull? sessionInfo)
    {
        if (sessionInfo == null)
            return null;

        return new SessionInfo(sessionInfo.IdPrefix) {
            Version = sessionInfo.Version,
            IsActive = sessionInfo.IsActive,
            CreatedAt = sessionInfo.CreatedAt,
            LastSeenAt = sessionInfo.LastSeenAt,
            ExpiresAt = sessionInfo.ExpiresAt,
            AuthenticatedIdentity = sessionInfo.AuthenticatedIdentity,
            UserId = sessionInfo.UserId,
            IPAddress = sessionInfo.IPAddress,
            Description = sessionInfo.Description,
        };
    }

    public static UserId? GetGuestId(this SessionInfoFull? sessionInfo)
    {
        if (sessionInfo is null)
            return null;

        var guestId = sessionInfo.GuestId;
        return guestId is { IsGuest: true } ? guestId : null;
    }
}
