using ActualChat.UI.Blazor.Services;
using Microsoft.AspNetCore.Components;

namespace ActualChat.App.Maui.Services;

internal sealed class MauiClientAuth : IClientAuth
{
    private ClientAppSettings AppSettings { get; }
    private UrlMapper UrlMapper { get; }
    private ILogger<MauiClientAuth> Log { get; }
    private MobileAuthClient MobileAuthClient { get; }
    private NavigationManager Nav { get; }

    public MauiClientAuth(IServiceProvider services)
    {
        AppSettings = services.GetRequiredService<ClientAppSettings>();
        UrlMapper = services.GetRequiredService<UrlMapper>();
        Log = services.GetRequiredService<ILogger<MauiClientAuth>>();
        MobileAuthClient = services.GetRequiredService<MobileAuthClient>();
        Nav = services.GetRequiredService<NavigationManager>();
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

        var uri = $"{UrlMapper.BaseUrl}mobileauth/signin/{AppSettings.SessionId}/{scheme}";
        await OpenSystemBrowserForSignIn(uri).ConfigureAwait(true);
        Nav.NavigateTo(Links.Chat(default), true);
    }

    public async ValueTask SignOut()
    {
#if ANDROID
        var activity = (MainActivity)Platform.CurrentActivity!;
        if (activity.IsSignedInWithGoogle()) {
            await activity.SignOutWithGoogle().ConfigureAwait(true);
        }
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
