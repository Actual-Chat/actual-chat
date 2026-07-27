namespace ActualChat.App.Maui.Services;

/// <summary>
/// Runs a web authentication flow in the platform's in-app browser
/// (<c>ASWebAuthenticationSession</c> on Apple platforms, Chrome Custom Tabs on Android),
/// returning once the server redirects to <see cref="MauiSettings.AuthCallbackUrl"/>.
/// </summary>
public sealed class MauiWebAuthenticator(IServiceProvider services)
{
    private ILogger Log { get; } = services.LogFor<MauiWebAuthenticator>();

    public async Task<bool> Run(string url, CancellationToken cancellationToken = default)
    {
        try {
            var options = new WebAuthenticatorOptions {
                Url = url.ToUri(),
                CallbackUrl = MauiSettings.AuthCallbackUrl.ToUri(),
                // Apple-only; keeps the flow out of the shared Safari cookie jar,
                // which also removes the system consent alert.
                PrefersEphemeralWebBrowserSession = true,
            };
            await WebAuthenticator.Default.AuthenticateAsync(options).WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is TaskCanceledException or OperationCanceledException) {
            Log.LogInformation("Web auth flow was canceled");
            return false;
        }
        catch (Exception e) {
            Log.LogError(e, "Web auth flow failed");
            return false;
        }
    }
}
