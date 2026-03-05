using ActualChat.Hosting;
using ActualChat.UI.Blazor.Module;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Provides a unified online/offline signal for the app.
/// Used by ReconnectUI (RPC) and AudioStreamer (SignalR) to skip reconnects
/// while offline and reconnect immediately when online.
/// </summary>
public abstract class ConnectivityUI : UIWorkerBase<UIHub>
{
    protected static readonly string JSInitMethod
        = $"{BlazorUICoreModule.ImportName}.ConnectivityUI.init";
    private static readonly string JSSetOnlineMethod
        = $"{BlazorUICoreModule.ImportName}.ConnectivityUI.setOnline";
    protected static readonly string JSSetConnectedMethod
        = $"{BlazorUICoreModule.ImportName}.ConnectivityUI.setConnected";

    private readonly MutableState<bool> _isConnected;
    private bool _jsIsOnline = true; // Must be in sync with the default value for _jsIsOnline in JS!
    private bool _jsIsConnected = true; // Must be in sync with the default value for _isConnected in JS!

    protected bool MustPushIsOnlineToJS { get; init; }

    public bool IsBlazorServer { get; }
    public IState<bool> IsConnected => _isConnected;
    public RpcClientPeer? Peer => IsBlazorServer ? null : Hub.RpcHub.GetClientPeer(RpcPeerRef.Default);
    public abstract IState<bool> IsOnline { get; }

    protected ConnectivityUI(UIHub hub) : base(hub)
    {
        IsBlazorServer = hub.HostInfo.HostKind.IsServer();
        _isConnected = StateFactory.NewMutable(IsBlazorServer);
    }

    public Task WhenConnected(CancellationToken cancellationToken = default)
        => IsConnected.Computed.When(x => x, cancellationToken);

    public Task WhenDisconnected(CancellationToken cancellationToken = default)
        => IsConnected.Computed.When(x => !x, cancellationToken);

    // Protected methods

    protected async Task Initialize(DotNetObjectReference<IConnectivityUIBackend>? backendRef = null)
    {
        if (Hub.Services.GetService<RpcClientPeerReconnectDelayer>() is AppRpcClientPeerReconnectDelayer delayer)
            delayer.IsOnlineDetector = () => !IsOnline.IsValue(out var v) || v;
        try {
            await JS.InvokeVoidAsync(JSInitMethod, backendRef, IsBlazorServer).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Initialize failed");
        }
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        if (IsBlazorServer)
            return Task.CompletedTask;

        var baseChains = new[] {
            AsyncChain.From(PushIsOnlineToJS),
            AsyncChain.From(PushIsConnectedToJS),
            AsyncChain.From(ResetReconnectDelaysWhenComeOnline),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return baseChains
            .Select(chain => chain.Log(LogLevel.Debug, Log).RetryForever(retryDelays, Log))
            .RunIsolated(cancellationToken);
    }

    // Private methods

    private async Task PushIsOnlineToJS(CancellationToken cancellationToken)
    {
        if (!MustPushIsOnlineToJS)
            return;

        var changes = IsOnline.Computed.Changes(FixedDelayer.NextTick, cancellationToken);
        await foreach (var (isOnline, _) in changes.ConfigureAwait(false)) {
            if (isOnline == _jsIsOnline)
                continue;

            await Hub.JS.InvokeVoidAsync(JSSetOnlineMethod, CancellationToken.None, isOnline).ConfigureAwait(false);
            _jsIsOnline = isOnline;
        }
    }

    private async Task PushIsConnectedToJS(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var peer = Peer!;
            var connectionState = peer.ConnectionState;
            var isConnected = connectionState.Value.Handshake is not null;
            _isConnected.Set(isConnected);
            if (isConnected != _jsIsConnected) {
                await JS.InvokeVoidAsync(JSSetConnectedMethod, CancellationToken.None, isConnected).ConfigureAwait(false);
                _jsIsConnected = isConnected;
            }
            await connectionState.WhenNext(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ResetReconnectDelaysWhenComeOnline(CancellationToken cancellationToken)
    {
        if (IsBlazorServer)
            return;

        while (!cancellationToken.IsCancellationRequested) {
            await IsOnline.Computed.When(x => !x, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Offline");
            await IsOnline.Computed.When(x => x, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Online");
            Hub.ReconnectUI.ResetReconnectDelays();
        }
    }
}
