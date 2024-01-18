using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiClientAuth))]
internal sealed class MauiClientAuth(UIHub hub) : IClientAuth
{
    private SessionTokens? _sessionTokens;
    private ILogger? _log;

    private UIHub Hub { get; } = hub;
    private SessionTokens SessionTokens => _sessionTokens ??= Hub.GetRequiredService<SessionTokens>();
    private HostInfo HostInfo => Hub.HostInfo();
    private ILogger Log => _log ??= Hub.LogFor(GetType());

    public async Task SignIn(string schema)
    {
        if (schema.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(schema));

#if ANDROID
        if (OrdinalEquals(IClientAuth.GoogleSchemeName, schema)) {
            var googleAuth = Hub.GetRequiredService<NativeGoogleAuth>();
            if (googleAuth.IsAvailable()) {
                await googleAuth.SignIn().ConfigureAwait(false);
                return;
            }
        }
#endif
#if IOS
        if (OrdinalEquals(IClientAuth.AppleIdSchemeName, schema)
            && DeviceInfo.Platform == DevicePlatform.iOS
            && DeviceInfo.Version.Major >= 13)
        {
            var appleAuth = Hub.GetRequiredService<NativeAppleAuth>();
            await appleAuth.SignIn().ConfigureAwait(false);
            return;
        }
#endif

        await WebSignInOrSignOut($"sign-in/{schema}").ConfigureAwait(false);
    }

    public async Task SignOut()
    {
#if ANDROID
        var googleAuth = Hub.GetRequiredService<NativeGoogleAuth>();
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
        if (HostInfo.AppKind == AppKind.Ios)
            Array.Reverse(schemas);
        return ValueTask.FromResult(schemas);
    }

    // Private methods

    private async Task WebSignInOrSignOut(string endpoint)
    {
        var isSignIn = endpoint.OrdinalIgnoreCaseStartsWith("sign-in");
        try {
            var sessionToken = await SessionTokens.Get().ConfigureAwait(true);
            var url = $"{MauiSettings.BaseUrl}maui-auth/{endpoint}?s={sessionToken.Token.UrlEncode()}";
            if (MauiSettings.WebAuth.UseSystemBrowser) {
                await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred).ConfigureAwait(false);
                // NOTE(AY): WebView crashes on the call below in Android:
                // await History.OpenNewWindow(url).ConfigureAwait(false);
                return;
            }

            // WebView-based authentication
            var returnUrl = Hub.UrlMapper().ToAbsolute( isSignIn ? Links.Chats : Links.Home);
            // NOTE(AY): returnUrl here points to https://[xxx.]actual.chat/xxx ,
            // but MauiNavigationInterceptor will correct it to the local one anyway.
            url = $"{url}&returnUrl={returnUrl.UrlEncode()}";
            Hub.Nav.NavigateTo(url);
        }
        catch (Exception ex) {
            Log.LogError(ex, "WebSignInOrSignOut failed (endpoint: {Endpoint})", endpoint);
        }
    }
}
