namespace ActualChat.Resilience;

/// <summary>
/// Resolves the user a session belongs to. A null result means "no user yet".
/// Required in any host whose policy charges <see cref="RateLimitIdentityKind.UserId"/>.
/// </summary>
public delegate ValueTask<UserId?> RateLimitUserIdResolver(Session session, CancellationToken cancellationToken);
