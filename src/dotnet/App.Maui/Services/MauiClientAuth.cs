using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

internal sealed class MauiClientAuth : IClientAuth
{
    private ILogger? _log;
    private HostInfo? _hostInfo;
    private History? _history;

    private IServiceProvider Services { get; }
    private HostInfo HostInfo => _hostInfo ??= Services.GetRequiredService<HostInfo>();
    private History History => _history ??= Services.GetRequiredService<History>();
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

        await SignInOrSignOut($"signIn/{schema}").ConfigureAwait(false);
    }

    public async ValueTask SignOut()
    {
#if ANDROID
        var androidGoogleSignIn = Services.GetRequiredService<AndroidGoogleSignIn>();
        if (androidGoogleSignIn.IsSignedIn())
            await androidGoogleSignIn.SignOut().ConfigureAwait(true);
#endif

        await SignInOrSignOut("signOut").ConfigureAwait(false);
    }

    public ValueTask<(string Name, string DisplayName)[]> GetSchemas()
    {
        var schemas = new[] {
            (IClientAuth.GoogleSchemeName, "Google"),
            (IClientAuth.AppleIdSchemeName, "Apple"),
        };
        if (HostInfo.ClientKind == ClientKind.Ios)
            Array.Reverse(schemas);
        return ValueTask.FromResult(schemas);
    }

    // Private methods

    private async Task SignInOrSignOut(string endpoint)
    {
        var isSignIn = endpoint.OrdinalIgnoreCaseStartsWith("signIn");
        try {
            var sessionId = Services.GetRequiredService<Session>().Id.Value;
            var url = $"{MauiSettings.BaseUrl}mobileAuthV2/{endpoint}?s={sessionId.UrlEncode()}";
            if (MauiSettings.SignIn.UseWebView) {
                var returnUrl = History.Nav.ToAbsoluteUri(isSignIn ? Links.Chats : Links.Home).ToString();
                url = $"{url}&returnUrl={returnUrl.UrlEncode()}";
                History.Nav.NavigateTo(url);
            }
            else
                await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred).ConfigureAwait(false);
        }
        catch (Exception ex) {
            Log.LogError(ex, "SignInOrSignOut failed (endpoint: {Endpoint})", endpoint);
            throw;
        }
    }
}
