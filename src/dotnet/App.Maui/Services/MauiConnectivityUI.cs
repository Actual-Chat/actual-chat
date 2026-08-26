using ActualChat.Rpc;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// MAUI implementation: uses Microsoft.Maui.Networking.IConnectivity for network state,
/// and pushes state to JS via ConnectivityUI.setOnline().
/// </summary>
public sealed class MauiConnectivityUI : ConnectivityUI
{
    private readonly MutableState<bool> _isOnline;
    private string _connectionProfiles;

    public override IState<bool> IsOnline => _isOnline;

    public MauiConnectivityUI(UIHub hub) : base(hub)
    {
        MustPushIsOnlineToJS = true;
        var connectivity = Connectivity.Current;
        var isOnline = connectivity.NetworkAccess.IsOnline();
        _isOnline = hub.StateFactory.NewMutable(isOnline, StateCategories.Get(GetType(), nameof(IsOnline)));
        _connectionProfiles = connectivity.ConnectionProfiles.ToDelimitedString();
        connectivity.ConnectivityChanged += OnConnectivityChanged;
        hub.RegisterDisposable((Action)(() => connectivity.ConnectivityChanged -= OnConnectivityChanged));
        _ = Initialize();
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        // A different network makes every earlier verdict meaningless, so the endpoints get
        // re-measured. The current one is kept until that finishes: dropping to an unproven
        // origin costs a reconnect, and strands the app there when the origin is the bad one.
        var profiles = e.ConnectionProfiles.ToDelimitedString();
        if (profiles != _connectionProfiles) {
            _connectionProfiles = profiles;
            if (RpcEndpointSelector.Instance is { } endpointSelector) {
                endpointSelector.Invalidate();
                Log.LogInformation("Network changed to {Profiles}, RPC endpoints will be re-measured", profiles);
            }
        }
        _isOnline.Value = e.NetworkAccess.IsOnline();
    }
}
