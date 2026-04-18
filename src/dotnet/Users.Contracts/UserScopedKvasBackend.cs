namespace ActualChat.Users;

/// <summary>
/// User-scoped KVAS backed by <see cref="IServerKvasBackend"/>.
/// </summary>
public sealed class UserScopedKvasBackend(IServerKvasBackend serverKvasBackend, UserId userId)
    : ServerKvasBackendClient(serverKvasBackend, GetUserPrefix(userId.Require()))
{
    public UserId UserId { get; } = userId.Require();

    [return: NotNullIfNotNull(nameof(userId))]
    public static string? GetUserPrefix(UserId? userId)
        => userId is null
            ? null
            : userId.IsGuest
                ? $"g/{userId}/"
                : $"u/{userId}/";
}
