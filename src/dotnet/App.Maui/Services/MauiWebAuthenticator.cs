namespace ActualChat.App.Maui.Services;

/// <summary>
/// Runs a web authentication flow in the platform's in-app browser
/// (<c>ASWebAuthenticationSession</c> on Apple platforms, Chrome Custom Tabs on Android),
/// returning once the server redirects to <see cref="MauiSettings.AuthCallbackUrl"/>.
/// Windows has no in-app browser, so it falls back to the default browser plus
/// protocol activation.
/// </summary>
public sealed class MauiWebAuthenticator(IServiceProvider services)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private ILogger Log { get; } = services.LogFor<MauiWebAuthenticator>();

    public async Task<bool> Run(string url, CancellationToken cancellationToken = default)
    {
        try {
#if WINDOWS
            return await RunWindows(url, cancellationToken).ConfigureAwait(false);
#else
            var options = new WebAuthenticatorOptions {
                Url = url.ToUri(),
                CallbackUrl = MauiSettings.AuthCallbackUrl.ToUri(),
                // Apple-only; keeps the flow out of the shared Safari cookie jar,
                // which also removes the system consent alert.
                PrefersEphemeralWebBrowserSession = true,
            };
            await WebAuthenticator.Default.AuthenticateAsync(options, cancellationToken).ConfigureAwait(false);
            return true;
#endif
        }
        catch (Exception e) when (e is TaskCanceledException or OperationCanceledException or TimeoutException) {
            Log.LogInformation("Web auth flow was canceled or timed out");
            return false;
        }
        catch (Exception e) {
            Log.LogError(e, "Web auth flow failed");
            return false;
        }
    }

#if WINDOWS
    // Private methods

    private async Task<bool> RunWindows(string url, CancellationToken cancellationToken)
    {
        WindowsAppScheme.EnsureRegistered();
        var callbackSource = TaskCompletionSourceExt.New<bool>();
        void OnActivated(string arguments) {
            if (arguments.StartsWith(MauiSettings.AuthCallbackUrl, StringComparison.OrdinalIgnoreCase))
                callbackSource.TrySetResult(true);
        }

        WinUI.App.AppInstanceActivated += OnActivated;
        try {
            await Browser.Default.OpenAsync(url, BrowserLaunchMode.External).ConfigureAwait(false);
            return await callbackSource.Task.WaitAsync(Timeout, cancellationToken).ConfigureAwait(false);
        }
        finally {
            WinUI.App.AppInstanceActivated -= OnActivated;
        }
    }
#endif
}
