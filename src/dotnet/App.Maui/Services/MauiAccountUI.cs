using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiAccountUI))]
public class MauiAccountUI(UIHub hub) : AccountUI(hub)
{
    private const string SignInFailedMessage = "Sign-in failed. Please check your connection and try again.";

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

        await WebSignIn($"/signIn/{schema}").ConfigureAwait(false);
    }

    protected override async Task SignOutBackend()
    {
        // Callers are fire-and-forget, so a throw here would be an unobserved task exception.
        try {
#if ANDROID
            var googleAuth = Hub.Services.GetRequiredService<NativeGoogleAuth>();
            if (googleAuth.IsSignedIn())
                await googleAuth.SignOut().ConfigureAwait(true);
#endif

            await Hub.Services.Commander().Call(new NativeAuth_SignOut(Session)).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "SignOutBackend failed");
        }
    }

    // Private methods

    private async Task WebSignIn(string endpoint)
    {
        var mustReportError = false;
        try {
            var sessionToken = await Hub.SessionTokens.Get(TimeSpan.FromMinutes(15)).ConfigureAwait(true);
            var url = $"{MauiSettings.BaseUrl}maui-auth/start"
                + $"?s={sessionToken.Token.UrlEncode()}"
                + $"&e={endpoint.UrlEncode()}"
                + $"&flow={"Sign-in".UrlEncode()}"
                + $"&appKind={HostInfo.AppKind:G}"
                + $"&redirectUrl={MauiSettings.AuthCallbackUrl.UrlEncode()}";
            var webAuthenticator = Hub.Services.GetRequiredService<MauiWebAuthenticator>();
            var result = await webAuthenticator.Run(url, endpoint).ConfigureAwait(false);
            mustReportError = result == WebAuthResult.Failed;
        }
        catch (Exception e) {
            Log.LogError(e, "WebSignIn failed (endpoint: {Endpoint})", endpoint);
            mustReportError = true;
        }
        if (mustReportError)
            await SetSignInError(SignInFailedMessage).SilentAwait(false);
    }

    private Task SetSignInError(string error)
    {
        var command = new SessionTemporals_Set(Session, Constants.SessionTemporals.SignInErrorKey, error);
        return Hub.Services.Commander().Run(command, true, CancellationToken.None);
    }
}
