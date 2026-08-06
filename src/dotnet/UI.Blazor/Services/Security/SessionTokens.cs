using ActualChat.Security;
using ActualChat.UI.Blazor.Module;
using ActualLab.Locking;

namespace ActualChat.UI.Blazor.Services;

public sealed class SessionTokens(UIHub hub) : UIWorkerBase<UIHub>(hub), IComputeService
{
    public static readonly TimeSpan InfLifespan = TimeSpan.FromDays(365);
    public static readonly TimeSpan DefaultLifespan = SecureToken.Lifespan;
    public static readonly TimeSpan RefreshPeriod = DefaultLifespan / 2;

    private static readonly string JSSetMethod = $"{BlazorUICoreModule.ImportName}.SessionTokens.set";

    private readonly AsyncLock _asyncLock = new(LockReentryMode.CheckedFail);
    private volatile SecureToken? _lastToken;

    private ISecureTokens SecureTokens => field ??= Services.GetRequiredService<ISecureTokens>();
    private DeviceAwakeUI DeviceAwakeUI => field ??= Services.GetRequiredService<DeviceAwakeUI>();
    private Moment SystemNow => Clocks.SystemClock.Now;
    private Moment ServerNow => Clocks.ServerClock.Now;
    private SecureToken? LastToken => _lastToken;

    public async ValueTask<SecureToken> Get(TimeSpan minLifespan, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minLifespan, SecureToken.Lifespan);

        if (TryUseLastToken(minLifespan) is { } token)
            return token;

        using var releaser = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        releaser.MarkLockedLocally();

        if (TryUseLastToken(minLifespan) is { } token1)
            return token1;

        token = await SecureTokens.CreateSessionToken(Session, cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _lastToken, token);
        return token;
    }

    // Protected & private methods

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(0.1, 5);
        return AsyncChain.From(AutoRefresh)
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .RunIsolated(cancellationToken);
    }

    private SecureToken? TryUseLastToken(TimeSpan minLifespan)
        => LastToken is { } token && token.ExpiresAt >= ServerNow + minLifespan
            ? token
            : null;

    private async Task AutoRefresh(CancellationToken cancellationToken)
    {
        if (HostInfo.AppKind is not (AppKind.Ios or AppKind.MacOS)) {
            var expiresAtMs = (long)(SystemNow.EpochOffset + InfLifespan).TotalMilliseconds;
            await SetJSToken("", expiresAtMs, cancellationToken).ConfigureAwait(false);
            return;
        }

        var jsToken = "";
        while (!cancellationToken.IsCancellationRequested) {
            var current = await Get(DefaultLifespan, cancellationToken).ConfigureAwait(false);
            if (jsToken != current.Token) {
                var expiresIn = (current.ExpiresAt - ServerNow).Positive();
                var expiresAtMs = (long)(SystemNow.EpochOffset + expiresIn).TotalMilliseconds;
                await SetJSToken(current.Token, expiresAtMs, cancellationToken).ConfigureAwait(false);
                jsToken = current.Token;
            }
            await DeviceAwakeUI.Sleep(RefreshPeriod, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SetJSToken(string token, long expiresAtMs, CancellationToken cancellationToken)
        => await JS.InvokeVoidAsync(JSSetMethod, CancellationToken.None, token, expiresAtMs)
            .AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
}
