using ActualChat.Security;
using ActualLab.Locking;

namespace ActualChat.UI.Blazor.Services;

public sealed class SessionTokens(UIHub hub) : UIServiceBase<UIHub>(hub)
{
    private readonly AsyncLock _asyncLock = new(LockReentryMode.CheckedFail);
    private volatile SecureToken? _current;

    private ISecureTokens SecureTokens => field ??= Services.GetRequiredService<ISecureTokens>();
    private MomentClock ServerClock => field ??= Clocks.ServerClock;

    public TimeSpan MinLifespan { get; init; } = TimeSpan.FromMinutes(60);

    public ValueTask<SecureToken> Get(CancellationToken cancellationToken = default)
        => Get(MinLifespan, cancellationToken);

    // Private methods

    private async ValueTask<SecureToken> Get(TimeSpan minLifespan, CancellationToken cancellationToken = default)
    {
        minLifespan = minLifespan
            .Add(TimeSpan.FromMinutes(1))
            .Clamp(default, SecureToken.Lifespan / 2);
        var minExpiresAt = ServerClock.Now + minLifespan;
        var result = _current;
        if (result != null && result.ExpiresAt >= minExpiresAt)
            return result;

        result = await GetNew(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async ValueTask<SecureToken> GetNew(CancellationToken cancellationToken = default)
    {
        using var releaser = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        releaser.MarkLockedLocally();

        var result = _current;
        if (result != null && result.ExpiresAt >= ServerClock.Now + (SecureToken.Lifespan / 2))
            return result;

        result = await SecureTokens.CreateSessionToken(Session, cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _current, result);
        return result;
    }
}
