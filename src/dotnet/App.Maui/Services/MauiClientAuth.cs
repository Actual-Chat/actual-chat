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

    public async Task SignIn(string schema)
    {
        if (schema.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(schema));

#if ANDROID
        if (OrdinalEquals(IClientAuth.GoogleSchemeName, schema)) {
            var googleAuth = Services.GetRequiredService<NativeGoogleAuth>();
            await googleAuth.SignIn().ConfigureAwait(false);
            return;
        }
#endif
#if IOS
        if (OrdinalEquals(IClientAuth.AppleIdSchemeName, schema)
            && DeviceInfo.Platform == DevicePlatform.iOS
            && DeviceInfo.Version.Major >= 13)
        {
            var appleAuth = Services.GetRequiredService<NativeAppleAuth>();
            await appleAuth.SignIn().ConfigureAwait(false);
            return;
        }
#endif

        await WebSignInOrSignOut($"sign-in/{schema}").ConfigureAwait(false);
    }

    public async Task SignOut()
    {
#if ANDROID
        var googleAuth = Services.GetRequiredService<NativeGoogleAuth>();
        if (googleAuth.IsSignedIn())
            await googleAuth.SignOut().ConfigureAwait(true);
#endif

        await WebSignInOrSignOut("sign-out").ConfigureAwait(false);
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

    private async Task WebSignInOrSignOut(string endpoint)
    {
        var isSignIn = endpoint.OrdinalIgnoreCaseStartsWith("sign-in");
        try {
            var sessionId = Services.Session().Id.Value;
            var url = $"{MauiSettings.BaseUrl}maui-auth/{endpoint}?s={sessionId.UrlEncode()}";
            if (MauiSettings.WebAuth.UseSystemBrowser) {
                await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred).ConfigureAwait(false);
                return;
            }

            // WebView-based authentication
            var returnUrl = History.UrlMapper.ToAbsolute( isSignIn ? Links.Chats : Links.Home);
            // NOTE(AY): returnUrl here points to https://[xxx.]actual.chat/xxx ,
            // but MauiNavigationInterceptor will correct it to the local one anyway.
            url = $"{url}&returnUrl={returnUrl.UrlEncode()}";
            History.Nav.NavigateTo(url);
        }
        catch (Exception ex) {
            Log.LogError(ex, "WebSignInOrSignOut failed (endpoint: {Endpoint})", endpoint);
        }
    }
}
