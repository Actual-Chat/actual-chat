using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Microsoft.AspNetCore.Components.WebView;

namespace ActualChat.App.Maui;

public partial class MauiWebView
{
    // ReSharper disable once CollectionNeverUpdated.Local
    private static readonly HashSet<string> AllowedExternalHosts = new() { "www.youtube.com" };

    public static readonly Uri BaseLocalUri = new($"https://{MauiSettings.LocalHost}/");
    public Uri LastUri { get; private set; } = BaseLocalUri;
    public Uri LastLocalUri { get; private set; } = BaseLocalUri;
    public bool IsOnLocalUri => LastUri == LastLocalUri;

    public async Task NavigateTo(string uri, bool hardReload = false)
    {
        if (!hardReload && ScopedServices is { } scopedServices) {
            // Soft navigation
            try {
                var hub = scopedServices.GetRequiredService<UIHub>();
                await hub.Dispatcher.InvokeSafeAsync(() => hub.Nav.NavigateTo(uri), Log).ConfigureAwait(false);
                return;
            }
            catch (Exception e) {
                Log.LogError(e, "Soft NavigateTo failed, retrying with hard navigation...");
            }
        }

        HardNavigateTo(uri);
    }

    // Private methods

    private void OnLoading(object? sender, UrlLoadingEventArgs eventArgs)
    {
        if (IsDead && Current == this) {
            BeginDispatchToMainThread(() => {
                if (Current == this)
                    MainPage.Current.RecreateWebView();
            }, allowInline: false);
            eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
            return;
        }
        #if false
        // NOTE(DF): MauiLivenessProbe is switched off for now.
        if (LastResumeAt.Elapsed < TimeSpan.FromSeconds(0.5))
            MauiLivenessProbe.Check();
        #endif

        var uri = eventArgs.Url;
        var isLocalUri = HandleLoading(uri, eventArgs);
        Tracer.Point($"{nameof(HandleLoading)}: Url: '{uri}' -> {eventArgs.UrlLoadingStrategy}, {(isLocalUri ? "local" : "external")}");
        if (eventArgs.UrlLoadingStrategy != UrlLoadingStrategy.OpenInWebView)
            return;

        LastUri = uri;
        if (isLocalUri)
            LastLocalUri = uri;
    }

    private bool HandleLoading(Uri uri, UrlLoadingEventArgs eventArgs)
    {
        var wasOnLocalUri = IsOnLocalUri;
        if (uri.Host == MauiSettings.LocalHost) {
            // Local MAUI app URL
            eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.OpenInWebView;
            return true;
        }

        if (!MauiSettings.BaseUri.IsBaseOf(uri)) {
            // Neither local MAUI app URL nor host URL
            var isAllowedExternalUri = AllowedExternalHosts.Contains(uri.Host);
            eventArgs.UrlLoadingStrategy = isAllowedExternalUri
                ? UrlLoadingStrategy.OpenInWebView
                : UrlLoadingStrategy.OpenExternally;
            // TODO: Remove this workaround when MAUI issue is fixed: https://github.com/dotnet/maui/issues/25602
            if (OperatingSystem.IsIOSVersionAtLeast(18) && eventArgs.UrlLoadingStrategy == UrlLoadingStrategy.OpenExternally)
                _ = ForegroundTask.Run(() => Browser.Default.OpenAsync(uri, BrowserLaunchMode.External));
            return false;
        }

        // If we're here, it's a host URL, so we have to re-route it to the local one
        var localUri = HostToAbsoluteLocalUri(uri);
        BeginDispatchToMainThread(
            () => _ = NavigateTo(localUri, !wasOnLocalUri),
            allowInline: false);
        eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
        return false;
    }

    private static string HostToAbsoluteLocalUri(Uri hostUri)
    {
        var relativeUri = MauiSettings.BaseUri.MakeRelativeUri(hostUri);
        return RelativeToAbsoluteLocalUri(relativeUri.ToString());
    }

    private static string RelativeToAbsoluteLocalUri(string relativeUri)
        => new Uri(BaseLocalUri, relativeUri).ToString();
}
