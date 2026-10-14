using System.Runtime.CompilerServices;
using ActualChat.Rpc;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// What the RPC path manager needs from the app: the network the device is on, or null when
/// nothing can be checked now. A backgrounded app has its networking suspended, so a failure seen
/// there says nothing about a host. BackgroundActive is different: a foreground service keeps
/// the network.
/// </summary>
public sealed class RpcNetworkInfoProvider : IRpcNetworkInfoProvider, IDisposable
{
    private readonly ConnectivityUI _connectivityUI;
    private readonly IAppActivityState _activityState;
    private readonly RpcEndpointSelector _selector;
    private readonly Func<string> _getNetworkId;
    private TaskCompletionSource _networkChanged = TaskCompletionSourceExt.New();

    private bool IsReady => _connectivityUI.IsOnline.Value
        && _activityState.State.Value != AppActivityState.BackgroundIdle;

    public RpcNetworkInfoProvider(
        ConnectivityUI connectivityUI,
        IAppActivityState activityState,
        RpcEndpointSelector selector,
        Func<string> getNetworkId)
    {
        _connectivityUI = connectivityUI;
        _activityState = activityState;
        _selector = selector;
        _getNetworkId = getNetworkId;
        _selector.NetworkChanged += OnNetworkChanged;
    }

    public void Dispose()
        => _selector.NetworkChanged -= OnNetworkChanged;

    public async IAsyncEnumerable<RpcNetworkInfo?> Watch([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var isFirst = true;
        RpcNetworkInfo? last = null;
        while (true) {
            // Taken before the state is read, so a change in between is not missed
            var networkChangedTask = Volatile.Read(ref _networkChanged).Task;
            var onlineComputed = _connectivityUI.IsOnline.Computed;
            var activityComputed = _activityState.State.Computed;
            var info = IsReady ? new RpcNetworkInfo(_getNetworkId.Invoke()) : null;
            if (isFirst || info != last) {
                isFirst = false;
                last = info;
                yield return info;
            }

            await Task.WhenAny(
                networkChangedTask,
                onlineComputed.WhenInvalidated(cancellationToken),
                activityComputed.WhenInvalidated(cancellationToken)
            ).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    // Private methods

    private void OnNetworkChanged()
        => Interlocked.Exchange(ref _networkChanged, TaskCompletionSourceExt.New()).TrySetResult();
}
