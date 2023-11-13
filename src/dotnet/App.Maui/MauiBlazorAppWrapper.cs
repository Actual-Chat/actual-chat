using ActualChat.Security;
using ActualChat.UI.Blazor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Stl.Fusion.Blazor;

namespace ActualChat.App.Maui;

/// <summary>
/// Prevents rendering MauiBlazorApp after WebView was marked as disconnected.
/// </summary>
/// <remarks>
/// After replacing WebView with a new one sometimes hard reload happens on a WebView at address '/chat/' on a replaced WebView.
/// This causes creating a new instance of MauiBlazorApp and setting ScopedServices.
/// In moment later new WebView will also load MauiBlazorApp which will try to set ScopedServices and will fail.
/// This failure will launch app reloading and this may repeat many times.
/// Preventing rendering MauiBlazorApp on disconnected WebView prevents conflicts in setting ScopedServices.
/// </remarks>
public class MauiBlazorAppWrapper : ComponentBase
{
    private MauiWebView? _mauiWebView;
    private Task? _whenDeactivated;

    protected override void OnInitialized()
        => _mauiWebView = MauiWebView.Current;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (!(_mauiWebView?.IsActive ?? false))
            return;

        _whenDeactivated ??= _mauiWebView.WhenDeactivated.ContinueWith(_ => StateHasChanged(), TaskScheduler.Current);
        builder.OpenComponent<MauiBlazorApp>(0);
        builder.CloseComponent();
    }
}
