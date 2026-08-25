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

    public override IState<bool> IsOnline => _isOnline;

    public MauiConnectivityUI(UIHub hub) : base(hub)
    {
        MustPushIsOnlineToJS = true;
        var connectivity = Connectivity.Current;
        var isOnline = connectivity.NetworkAccess.IsOnline();
        _isOnline = hub.StateFactory.NewMutable(isOnline, StateCategories.Get(GetType(), nameof(IsOnline)));
        connectivity.ConnectivityChanged += OnConnectivityChanged;
        hub.RegisterDisposable((Action)(() => connectivity.ConnectivityChanged -= OnConnectivityChanged));
        _ = Initialize();
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        // A different network is unproven, and it may well be unrestricted, so we always
        // go back to the origin and let the connection quality demote us again if needed.
        RpcEndpointSelector.Instance?.UseDirect();
        _isOnline.Value = e.NetworkAccess.IsOnline();
    }
}
