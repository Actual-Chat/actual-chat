using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiAccountUI))]
internal sealed class MauiAccountUI(UIHub hub) : AccountUI(hub)
{
    public override (string Name, string DisplayName)[] GetAuthSchemas()
    {
        var schemas = AuthSchema.AllExternal.AsEnumerable();
        if (HostInfo.AppKind == AppKind.Ios)
            schemas = schemas.Reverse();
        return AuthSchema.ToSchemasWithDisplayNames(schemas);
    }

    protected override async Task SignInBackend(string schema)
    {
        if (schema.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(schema));

#if ANDROID
        if (schema == AuthSchema.Google) {
            var googleAuth = Hub.Services.GetRequiredService<NativeGoogleAuth>();
            if (googleAuth.IsAvailable()) {
                await googleAuth.SignIn().ConfigureAwait(false);
                return;
            }
        }
#endif
#if IOS
        if (schema == AuthSchema.Apple
            && DeviceInfo.Platform == DevicePlatform.iOS
            && DeviceInfo.Version.Major >= 13)
        {
            var appleAuth = Hub.Services.GetRequiredService<NativeAppleAuth>();
            await appleAuth.SignIn().ConfigureAwait(false);
            return;
        }
#endif

        var endpoint = $"/signIn/{schema}";
        await WebSignInOrSignOut(endpoint, "Sign-in").ConfigureAwait(false);
    }

    protected override async Task SignOutBackend()
    {
#if ANDROID
        var googleAuth = Hub.Services.GetRequiredService<NativeGoogleAuth>();
        if (googleAuth.IsSignedIn())
            await googleAuth.SignOut().ConfigureAwait(true);
#endif

        await WebSignInOrSignOut("/signOut", "Sign-out").ConfigureAwait(false);
    }

    // Private methods

    private async Task WebSignInOrSignOut(string endpoint, string flowName)
    {
        var isSignIn = endpoint.StartsWith("/signIn", StringComparison.OrdinalIgnoreCase);
        try {
            var sessionToken = await Hub.SessionTokens.Get().ConfigureAwait(true);
            var url = $"{MauiSettings.BaseUrl}maui-auth/start"
                + $"?s={sessionToken.Token.UrlEncode()}"
                + $"&e={endpoint.UrlEncode()}"
                + $"&flow={flowName.UrlEncode()}"
                + $"&appKind={HostInfo.AppKind:G}";
            if (MauiSettings.WebAuth.UseSystemBrowser) {
                _ = MauiBrowser.Open(url);
                return;
            }

            // WebView-based authentication
            var redirectUrl = UrlMapper.ToAbsolute(isSignIn ? Links.Chats : Links.Home);
            // NOTE(AY): returnUrl here points to https://[xxx.]voxt.ai/xxx ,
            // but MauiNavigationInterceptor will correct it to the local one anyway.
            url = $"{url}&redirectUrl={redirectUrl.UrlEncode()}";
            Nav.NavigateTo(url);
        }
        catch (Exception ex) {
            Log.LogError(ex, "WebSignInOrSignOut failed (endpoint: {Endpoint})", endpoint);
        }
    }
}
