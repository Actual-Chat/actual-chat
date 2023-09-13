using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

public class BlazorWebViewDisconnectMarker(BlazorWebView blazorWebView)
{
    private readonly TaskCompletionSource _whenDisconnectedTaskSource = TaskCompletionSourceExt.New();

    public BlazorWebView BlazorWebView { get; } = blazorWebView;

    public bool IsDisconnected { get; private set; }

    public Task WhenDisconnected => _whenDisconnectedTaskSource.Task;

    public void MarkAsDisconnected()
    {
        IsDisconnected = true;
        _whenDisconnectedTaskSource.TrySetResult();
    }
}
