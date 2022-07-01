using ActualChat.UI.Blazor.Services;

namespace ActualChat.ClientApp.Services;

internal sealed class MauiClientAuth : IClientAuth
{
    private readonly ClientAppSettings _clientAppSettings;
    private readonly ILogger<MauiClientAuth> _log;

    public MauiClientAuth(ClientAppSettings clientAppSettings, ILogger<MauiClientAuth> log)
    {
        _clientAppSettings = clientAppSettings;
        _log = log;
    }

    public async ValueTask SignIn(string scheme)
    {
        var uri = $"{_clientAppSettings.BaseUri.EnsureSuffix("/")}mobileauth/signin/{MauiProgram.SessionId}/{scheme}";
        await OpenSystemBrowserForSignIn(uri).ConfigureAwait(true);
    }

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
