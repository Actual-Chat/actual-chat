namespace ActualChat.App.Maui.Services;

/// <summary>
/// Runs a web authentication flow in the platform's in-app browser, completing once
/// the server redirects to <see cref="MauiSettings.AuthCallbackUrl"/>. Windows has no
/// in-app browser, so it uses the default browser plus protocol activation.
/// </summary>
public sealed class MauiWebAuthenticator(IServiceProvider services)
{
#if WINDOWS
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);
#endif

    private ILogger Log { get; } = services.LogFor<MauiWebAuthenticator>();

    public async Task<WebAuthResult> Run(string url, string endpoint, CancellationToken cancellationToken = default)
    {
        // url carries a session token, so it must never be logged — endpoint is logged instead.
        try {
#if WINDOWS
            return await RunWindows(url, endpoint, cancellationToken).ConfigureAwait(false);
#else
            var options = new WebAuthenticatorOptions {
                Url = url.ToUri(),
                CallbackUrl = MauiSettings.AuthCallbackUrl.ToUri(),
                // Apple-only; keeps the flow out of the shared Safari cookie jar,
                // which also removes the system consent alert.
                PrefersEphemeralWebBrowserSession = true,
            };
            await WebAuthenticator.Default.AuthenticateAsync(options, cancellationToken).ConfigureAwait(false);
            return WebAuthResult.Completed;
#endif
        }
        catch (Exception e) when (e is TaskCanceledException or OperationCanceledException) {
            Log.LogInformation("Web auth flow was canceled (endpoint: {Endpoint}, appKind: {AppKind})",
                endpoint, MauiSettings.AppKind);
            return WebAuthResult.Cancelled;
        }
        catch (TimeoutException) {
            Log.LogWarning("Web auth flow timed out (endpoint: {Endpoint}, appKind: {AppKind})",
                endpoint, MauiSettings.AppKind);
            return WebAuthResult.Failed;
        }
        catch (Exception e) {
            Log.LogError(e, "Web auth flow failed (endpoint: {Endpoint}, appKind: {AppKind})",
                endpoint, MauiSettings.AppKind);
            return WebAuthResult.Failed;
        }
    }

#if WINDOWS
    // Private methods

    private async Task<WebAuthResult> RunWindows(string url, string endpoint, CancellationToken cancellationToken)
    {
        var callbackSource = TaskCompletionSourceExt.New<bool>();
        void OnActivated(string arguments) {
            if (arguments.StartsWith(MauiSettings.AuthCallbackUrl, StringComparison.OrdinalIgnoreCase))
                callbackSource.TrySetResult(true);
        }

        WinUI.App.AppInstanceActivated += OnActivated;
        try {
            if (!await MauiBrowser.Open(url).ConfigureAwait(false)) {
                Log.LogError("Failed to launch the browser for web auth (endpoint: {Endpoint})", endpoint);
                return WebAuthResult.Failed;
            }
            await callbackSource.Task.WaitAsync(Timeout, cancellationToken).ConfigureAwait(false);
            return WebAuthResult.Completed;
        }
        finally {
            WinUI.App.AppInstanceActivated -= OnActivated;
        }
    }
#endif
}

/// <summary>
/// The outcome of <see cref="MauiWebAuthenticator.Run"/>: <see cref="Cancelled"/> means
/// the user backed out, <see cref="Failed"/> means the flow never reached the server.
/// </summary>
public enum WebAuthResult
{
    Completed = 0,
    Cancelled,
    Failed,
}
