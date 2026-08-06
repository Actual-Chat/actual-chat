namespace ActualChat.Users;

/// <summary>
/// User-scoped KVAS backed by <see cref="IServerKvasBackend"/>.
/// </summary>
public sealed class UserScopedKvasBackend(
    IServerKvasBackend serverKvasBackend,
    UserId userId,
    bool isOutermost = false
    ) : ServerKvasBackendClient(serverKvasBackend, GetUserPrefix(userId.Require()), isOutermost)
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
