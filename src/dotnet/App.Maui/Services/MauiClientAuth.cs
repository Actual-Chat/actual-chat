using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

internal sealed class MauiClientAuth : IClientAuth
{
    private IServiceProvider Services { get; }
    private MobileAuthClient AuthClient { get; }
    private ILogger<MauiClientAuth> Log { get; }

    public MauiClientAuth(IServiceProvider services)
    {
        Services = services;
        Log = services.GetRequiredService<ILogger<MauiClientAuth>>();
        AuthClient = services.GetRequiredService<MobileAuthClient>();
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

        var session = await MauiSessionProvider.GetSession().ConfigureAwait(true);
        var sessionId = session.Id.Value;
        var uri = $"{AppSettings.BaseUrl}mobileauth/signin/{sessionId}/{scheme}";
        await OpenSystemBrowserForSignIn(uri).ConfigureAwait(false);
    }

    public async ValueTask SignOut()
    {
#if ANDROID
        var androidGoogleSignIn = Services.GetRequiredService<AndroidGoogleSignIn>();
        if (androidGoogleSignIn.IsSignedIn())
            await androidGoogleSignIn.SignOut().ConfigureAwait(true);
#endif
        await AuthClient.SignOut().ConfigureAwait(true);
    }

    public ValueTask<(string Name, string DisplayName)[]> GetSchemas()
        => ValueTask.FromResult(new[] {
            (IClientAuth.GoogleSchemeName, "Google"),
            (IClientAuth.FacebookSchemeName, "Facebook"),
        });

    private async Task OpenSystemBrowserForSignIn(string url)
    {
        try {
            await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred).ConfigureAwait(false);
        }
        catch (Exception ex) {
            Log.LogError(ex, "Failed to authenticate");
            throw;
        }
    }
}
