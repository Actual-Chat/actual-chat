using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

internal sealed class MauiClientAuth : IClientAuth
{
    private ClientAppSettings ClientAppSettings { get; }
    private ILogger<MauiClientAuth> Log { get; }

    public MauiClientAuth(ClientAppSettings clientAppSettings, ILogger<MauiClientAuth> log)
    {
        ClientAppSettings = clientAppSettings;
        Log = log;
    }

    public async ValueTask SignIn(string scheme)
    {
        if (OrdinalEquals(IClientAuth.GoogleSchemeName, scheme)) {
#if ANDROID
            var activity = (MainActivity)Platform.CurrentActivity!;
            await activity.SignInWithGoogle().ConfigureAwait(false);
            return;
#endif
        }

        var uri = $"{ClientAppSettings.BaseUri.EnsureEndsWith("/")}mobileauth/signin/{ClientAppSettings.SessionId}/{scheme}";
        await OpenSystemBrowserForSignIn(uri).ConfigureAwait(true);
    }

    public async ValueTask SignOut()
    {
#if ANDROID
        var activity = (MainActivity)Platform.CurrentActivity!;
        if (activity.IsSignedInWithGoogle()) {
            await activity.SignOutWithGoogle().ConfigureAwait(true);
            return;
        }
#endif

        var uri = $"{ClientAppSettings.BaseUri.EnsureEndsWith("/")}mobileauth/signout/{ClientAppSettings.SessionId}";
        await OpenSystemBrowserForSignIn(uri).ConfigureAwait(true);
    }

    public ValueTask<(string Name, string DisplayName)[]> GetSchemas()
        => ValueTask.FromResult(new[] {
            (IClientAuth.GoogleSchemeName, "Google"),
            (IClientAuth.FacebookSchemeName, "Facebook")
        });

    private async Task OpenSystemBrowserForSignIn(string url)
    {
        try {
            var uri = new Uri(url);
            await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred).ConfigureAwait(true);
        }
        catch (Exception ex) {
            Log.LogError(ex, "Failed to authenticate");
            throw;
        }
    }
}
