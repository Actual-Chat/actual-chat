using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

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
        var uri = $"{_clientAppSettings.BaseUri.EnsureSuffix("/")}mobileauth/signin/{_clientAppSettings.SessionId}/{scheme}";
        await OpenSystemBrowserForSignIn(uri).ConfigureAwait(true);
    }

    public async ValueTask SignOut()
    {
        var uri = $"{_clientAppSettings.BaseUri.EnsureSuffix("/")}mobileauth/signout/{_clientAppSettings.SessionId}";
        await OpenSystemBrowserForSignIn(uri).ConfigureAwait(true);
    }

    public ValueTask<(string Name, string DisplayName)[]> GetSchemas()
        => ValueTask.FromResult(new[]{ ("Google", "Google") });

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
