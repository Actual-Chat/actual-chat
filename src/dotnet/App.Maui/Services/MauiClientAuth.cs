using ActualChat.UI.Blazor.Services;
using Microsoft.AspNetCore.Components;

namespace ActualChat.App.Maui.Services;

internal sealed class MauiClientAuth : IClientAuth
{
    private MobileAuthClient? _mobileAuth;

    private IServiceProvider Services { get; }
    private MobileAuthClient MobileAuth => _mobileAuth ??= Services.GetRequiredService<MobileAuthClient>();

    private ILogger Log { get; }

    public MauiClientAuth(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
    }

    public async ValueTask SignIn(string schema)
    {
        if (schema.IsNullOrEmpty())
            throw new ArgumentException(nameof(schema));

        if (OrdinalEquals(IClientAuth.GoogleSchemeName, schema)) {
#if ANDROID
            var androidGoogleSignIn = Services.GetRequiredService<AndroidGoogleSignIn>();
            await androidGoogleSignIn.SignIn().ConfigureAwait(false);
            return;
#endif
        }

#if IOS
        if (OrdinalEquals(IClientAuth.AppleIdSchemeName, schema)
            && DeviceInfo.Platform == DevicePlatform.iOS
            && DeviceInfo.Version.Major >= 13)
        {
            var appleSignIn = Services.GetRequiredService<AppleSignIn>();
            await appleSignIn.SignIn().ConfigureAwait(false);
            return;
        }
#endif

        var nav = Services.GetRequiredService<NavigationManager>();
        var returnUrl = nav.ToAbsoluteUri(Links.Chats).ToString();
        nav.NavigateTo(
            $"{MauiSettings.BaseUrl}mobileAuthV2/signIn/{schema}?returnUrl={returnUrl.UrlEncode()}");
    }

    public async ValueTask SignOut()
    {
#if ANDROID
        var androidGoogleSignIn = Services.GetRequiredService<AndroidGoogleSignIn>();
        if (androidGoogleSignIn.IsSignedIn())
            await androidGoogleSignIn.SignOut().ConfigureAwait(true);
#endif

        var nav = Services.GetRequiredService<NavigationManager>();
        var returnUrl = nav.ToAbsoluteUri(Links.Home).ToString();
        nav.NavigateTo(
            $"{MauiSettings.BaseUrl}mobileAuthV2/signOut?returnUrl={returnUrl.UrlEncode()}");
    }

    public ValueTask<(string Name, string DisplayName)[]> GetSchemas()
    {
        var schemas = DeviceInfo.Platform == DevicePlatform.iOS
            ? new[] {
                (IClientAuth.AppleIdSchemeName, "Apple"),
                (IClientAuth.GoogleSchemeName, "Google"),
            }
            : new[] {
                (IClientAuth.GoogleSchemeName, "Google"),
                (IClientAuth.AppleIdSchemeName, "Apple"),
            };

        return ValueTask.FromResult(schemas);
    }

    // Private methods

    private async Task OpenInSystemBrowser(string url)
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
