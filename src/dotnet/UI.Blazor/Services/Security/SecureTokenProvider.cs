using ActualChat.Hosting;
using ActualChat.Security;

namespace ActualChat.UI.Blazor.Services;

public class SecureTokenProvider : WorkerBase, IComputeService
{
    private readonly IMutableState<SecureToken?> _currentToken;

    private HostInfo HostInfo { get; }
    private ISecureTokens SecureTokens { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public IState<SecureToken?> CurrentToken => _currentToken;
    public Task WhenInitialized { get; }

    public SecureTokenProvider(IServiceProvider services)
    {
        HostInfo = services.GetRequiredService<HostInfo>();
        SecureTokens = services.GetRequiredService<ISecureTokens>();
        Clocks = services.Clocks();
        Log = services.LogFor(GetType());
        _currentToken = services.StateFactory().NewMutable<SecureToken?>();

        WhenInitialized = Initialize();
        return;

        async Task Initialize()
        {
            _currentToken.Value = HostInfo.ClientKind == ClientKind.Ios
                ? await SecureTokens.CreateForDefaultSession(CancellationToken.None).ConfigureAwait(false)
                : null;
            this.Start();
        }
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new List<AsyncChain>();
        if (HostInfo.ClientKind == ClientKind.Ios)
            baseChains.Add(new(nameof(RefreshToken), RefreshToken));
        var retryDelays = RetryDelaySeq.Exp(0.1, 5);
        await (
            from chain in baseChains
            select chain
                .Log(LogLevel.Information, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken);
    }

    private async Task RefreshToken(CancellationToken cancellationToken)
    {
        var refreshAdvance = TimeSpan.FromMinutes(3);
        var clock = Clocks.ServerClock;
        while (!cancellationToken.IsCancellationRequested) {
            var token = _currentToken.ValueOrDefault;
            if (token == null) {
                // Recorder token isn't created yet or is never going to be created
                await clock.Delay(refreshAdvance, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var refreshAt = token.ExpiresAt - refreshAdvance;
            if (refreshAt >= clock.Now + TimeSpan.FromSeconds(0.1)) // 0.1s gap = just skip tiny waits
                await clock.Delay(refreshAt, cancellationToken).ConfigureAwait(false);

            token = await SecureTokens.CreateForDefaultSession(cancellationToken).ConfigureAwait(false);
            _currentToken.Value = token;
        }
    }
}
