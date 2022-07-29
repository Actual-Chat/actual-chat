using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

internal sealed class MauiClientAuth : IClientAuth
{
    private readonly ClientAppSettings _clientAppSettings;
    private readonly ILogger<MauiClientAuth> _log;

    private const string GoogleSchemeName = "Google";
    private const string FacebookSchemeName = "Facebook";

    public MauiClientAuth(ClientAppSettings clientAppSettings, ILogger<MauiClientAuth> log)
    {
        _clientAppSettings = clientAppSettings;
        _log = log;
    }

    public async ValueTask SignIn(string scheme)
    {
        if (string.Equals(GoogleSchemeName, scheme, StringComparison.Ordinal)) {
            #if ANDROID
            var activity = (MainActivity)Platform.CurrentActivity!;
            await activity.SignInWithGoogle().ConfigureAwait(false);
            return;
            #endif
        }

        var uri = $"{_clientAppSettings.BaseUri.EnsureSuffix("/")}mobileauth/signin/{_clientAppSettings.SessionId}/{scheme}";
        await OpenSystemBrowserForSignIn(uri).ConfigureAwait(true);
    }

    public async ValueTask SignOut()
    {
        #if ANDROID
        var activity = (MainActivity)Platform.CurrentActivity!;
        if (activity.IsSignedInWithGoogle()) {
            await activity.SignOutWithGoogle().ConfigureAwait(true);
            return;
        }
        #endif

        var uri = $"{_clientAppSettings.BaseUri.EnsureSuffix("/")}mobileauth/signout/{_clientAppSettings.SessionId}";
        await OpenSystemBrowserForSignIn(uri).ConfigureAwait(true);
    }

    public ValueTask<(string Name, string DisplayName)[]> GetSchemas()
        => ValueTask.FromResult(new[]{
                (GoogleSchemeName, "Google"),
                (FacebookSchemeName, "Facebook")
            });

    private async Task OpenSystemBrowserForSignIn(string url)
    {
        try {
            var uri = new Uri(url);
            await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred).ConfigureAwait(true);
        }
        catch (Exception ex) {
            _log.LogError(ex, "Failed to authenticate");
            throw;
        }
    }
}
