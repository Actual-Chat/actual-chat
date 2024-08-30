using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiAuth))]
internal sealed class MauiAuth(UIHub hub) : IClientAuth
{
    private SessionTokens? _sessionTokens;
    private ILogger? _log;

    private UIHub Hub { get; } = hub;
    private SessionTokens SessionTokens => _sessionTokens ??= Hub.GetRequiredService<SessionTokens>();
    private HostInfo HostInfo => Hub.HostInfo();
    private ILogger Log => _log ??= Hub.LogFor(GetType());

    public (string Name, string DisplayName)[] GetSchemas()
    {
        var schemas = AuthSchema.AllExternal.AsEnumerable();
        if (HostInfo.AppKind == AppKind.Ios)
            schemas = schemas.Reverse();
        return AuthSchema.ToSchemasWithDisplayNames(schemas);
    }

    public async Task SignIn(string schema)
    {
        if (schema.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(schema));

#if ANDROID
        if (OrdinalEquals(schema, AuthSchema.Google)) {
            var googleAuth = Hub.GetRequiredService<NativeGoogleAuth>();
            if (googleAuth.IsAvailable()) {
                await googleAuth.SignIn().ConfigureAwait(false);
                return;
            }
        }
#endif
#if IOS
        if (OrdinalEquals(schema, AuthSchema.Apple)
            && DeviceInfo.Platform == DevicePlatform.iOS
            && DeviceInfo.Version.Major >= 13)
        {
            var appleAuth = Hub.GetRequiredService<NativeAppleAuth>();
            await appleAuth.SignIn().ConfigureAwait(false);
            return;
        }
#endif

        await WebSignInOrSignOut($"/sign-in/{schema}", "Sign-in").ConfigureAwait(false);
    }

    public async Task SignOut()
    {
#if ANDROID
        var googleAuth = Hub.GetRequiredService<NativeGoogleAuth>();
        if (googleAuth.IsSignedIn())
            await googleAuth.SignOut().ConfigureAwait(true);
#endif

        await WebSignInOrSignOut("/sign-out", "Sign-out").ConfigureAwait(false);
    }

    // Private methods

    private async Task WebSignInOrSignOut(string endpoint, string flowName)
    {
        var isSignIn = endpoint.OrdinalIgnoreCaseStartsWith("sign-in");
        try {
            var sessionToken = await SessionTokens.Get().ConfigureAwait(true);
            var url = $"{MauiSettings.BaseUrl}maui-auth/start"
                + $"?s={sessionToken.Token.UrlEncode()}"
                + $"&e={endpoint.UrlEncode()}"
                + $"&flow={flowName.UrlEncode()}"
                + $"&appKind={HostInfo.AppKind:G}";
            if (MauiSettings.WebAuth.UseSystemBrowser) {
                await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred).ConfigureAwait(false);
                // NOTE(AY): WebView crashes on the call below in Android:
                // await History.OpenNewWindow(url).ConfigureAwait(false);
                return;
            }

            // WebView-based authentication
            var redirectUrl = Hub.UrlMapper().ToAbsolute( isSignIn ? Links.Chats : Links.Home);
            // NOTE(AY): returnUrl here points to https://[xxx.]actual.chat/xxx ,
            // but MauiNavigationInterceptor will correct it to the local one anyway.
            url = $"{url}&redirectUrl={redirectUrl.UrlEncode()}";
            Hub.Nav.NavigateTo(url);
        }
        catch (Exception ex) {
            Log.LogError(ex, "WebSignInOrSignOut failed (endpoint: {Endpoint})", endpoint);
        }
    }
}
