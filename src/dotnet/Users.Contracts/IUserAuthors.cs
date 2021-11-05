namespace ActualChat.Users;

/// <summary>
/// Returns source author properties (without any overrides) for the user.
/// </summary>
public interface IUserAuthors
{
    /// <summary>
    /// Returns source author properties (without any overrides) for the <paramref name="userId"/>.<br/>
    /// If <paramref name="userId"/> <c>== default</c> then <see langword="null" /> will be returned.
    /// </summary>
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserAuthor?> Get(UserId userId, CancellationToken cancellationToken);
}
