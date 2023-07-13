using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using Microsoft.AspNetCore.Components;

namespace ActualChat.App.Maui.Services;

internal sealed class MauiClientAuth : IClientAuth
{
    private ILogger? _log;
    private HostInfo? _hostInfo;

    private IServiceProvider Services { get; }
    private HostInfo HostInfo => _hostInfo ??= Services.GetRequiredService<HostInfo>();
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public MauiClientAuth(IServiceProvider services)
        => Services = services;

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

        var sessionId = Services.GetRequiredService<Session>().Id.Value;
        await OpenInBrowser($"{MauiSettings.BaseUrl}mobileAuthV2/signIn/{schema}?s={sessionId.UrlEncode()}")
            .ConfigureAwait(false);
    }

    public async ValueTask SignOut()
    {
#if ANDROID
        var androidGoogleSignIn = Services.GetRequiredService<AndroidGoogleSignIn>();
        if (androidGoogleSignIn.IsSignedIn())
            await androidGoogleSignIn.SignOut().ConfigureAwait(true);
#endif

        var sessionId = Services.GetRequiredService<Session>().Id.Value;
        await OpenInBrowser($"{MauiSettings.BaseUrl}mobileAuthV2/signOut?s={sessionId.UrlEncode()}")
            .ConfigureAwait(false);
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

    private async Task OpenInBrowser(string url)
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
