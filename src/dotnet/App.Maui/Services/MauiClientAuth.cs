using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

internal sealed class MauiClientAuth : IClientAuth
{
    private IServiceProvider Services { get; }
    private ClientAppSettings AppSettings { get; }
    private ILogger<MauiClientAuth> Log { get; }
    private MobileAuthClient MobileAuthClient { get; }

    public MauiClientAuth(IServiceProvider services)
    {
        Services = services;
        AppSettings = services.GetRequiredService<ClientAppSettings>();
        Log = services.GetRequiredService<ILogger<MauiClientAuth>>();
        MobileAuthClient = services.GetRequiredService<MobileAuthClient>();
    }

    public async ValueTask SignIn(string scheme)
    {
        if (scheme.IsNullOrEmpty()) throw new ArgumentException(nameof(scheme));

        if (OrdinalEquals(IClientAuth.GoogleSchemeName, scheme)) {
#if ANDROID
            var androidGoogleSignIn = Services.GetRequiredService<AndroidGoogleSignIn>();
            await androidGoogleSignIn.SignIn().ConfigureAwait(false);
            return;
#endif
        }

        var sessionId = await AppSettings.GetSessionId().ConfigureAwait(false);
        var uri = $"{AppSettings.BaseUrl}mobileauth/signin/{sessionId}/{scheme}";
        await OpenSystemBrowserForSignIn(uri).ConfigureAwait(true);
    }

    public async ValueTask SignOut()
    {
#if ANDROID
        var androidGoogleSignIn = Services.GetRequiredService<AndroidGoogleSignIn>();
        if (androidGoogleSignIn.IsSignedIn())
            await androidGoogleSignIn.SignOut().ConfigureAwait(true);
#endif
        await MobileAuthClient.SignOut().ConfigureAwait(true);
    }

    public ValueTask<(string Name, string DisplayName)[]> GetSchemas()
        => ValueTask.FromResult(new[] {
            (IClientAuth.GoogleSchemeName, "Google"),
            (IClientAuth.FacebookSchemeName, "Facebook")
        });

    private async Task OpenSystemBrowserForSignIn(string url)
    {
        try {
            await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred).ConfigureAwait(true);
        }
        catch (Exception ex) {
            Log.LogError(ex, "Failed to authenticate");
            throw;
        }
    }
}
