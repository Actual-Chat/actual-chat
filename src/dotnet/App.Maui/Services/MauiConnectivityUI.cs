using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Module;
using ActualChat.UI.Blazor.Services;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// MAUI implementation: uses Microsoft.Maui.Networking.IConnectivity for network state,
/// and pushes state to JS via ConnectivityUI.setOnline().
/// </summary>
public sealed class MauiConnectivityUI : ConnectivityUI
{
    private static readonly string JSSetOnlineMethod = $"{BlazorUICoreModule.ImportName}.ConnectivityUI.setOnline";

    private readonly MutableState<bool> _isOnline;

    public override IState<bool> IsOnline => _isOnline;

    public MauiConnectivityUI(UIHub hub) : base(hub)
    {
        var connectivity = Connectivity.Current;
        var isOnline = connectivity.NetworkAccess == NetworkAccess.Internet;
        _isOnline = hub.StateFactory.NewMutable(isOnline, StateCategories.Get(GetType(), nameof(IsOnline)));
        connectivity.ConnectivityChanged += OnConnectivityChanged;
        hub.RegisterDisposable((Action)(() => connectivity.ConnectivityChanged -= OnConnectivityChanged));
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        var isOnline = e.NetworkAccess is not (NetworkAccess.None or NetworkAccess.Local);
        _isOnline.Value = isOnline;
        _ = PushToJS(isOnline);
    }

    private async Task PushToJS(bool isOnline)
    {
        try {
            await Hub.JS.InvokeVoidAsync(JSSetOnlineMethod, isOnline).ConfigureAwait(false);
        }
        catch (Exception e) {
            Hub.LogFor<MauiConnectivityUI>().LogError(e, "Failed to push online state to JS");
        }
    }
}
