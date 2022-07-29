namespace ActualChat.App.Maui.Services;

public class MobileAuthClient
{
    private readonly ClientAppSettings _appSettings;
    private readonly ILogger<MobileAuthClient> _log;

    public MobileAuthClient(ClientAppSettings appSettings, ILogger<MobileAuthClient> log)
    {
        _appSettings = appSettings;
        _log = log;
    }

    public async Task<bool> SignInGoogle(string code)
    {
        if (string.IsNullOrEmpty(code))
            throw new ArgumentException($"'{nameof(code)}' cannot be null or empty.", nameof(code));
        var sessionId = _appSettings.SessionId;
        var requestUri = _appSettings.BaseUri.EnsureSuffix("/") + $"mobileAuth/signInGoogleWithCode/{sessionId.UrlEncode()}/{code.UrlEncode()}";
        try {
            var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(requestUri).ConfigureAwait(true);
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) {
            _log.LogError("Failed to sign in google", e);
            return false;
        }
    }

    public async Task<bool> SignOut()
    {
        var sessionId = _appSettings.SessionId;
        var requestUri = _appSettings.BaseUri.EnsureSuffix("/") + $"mobileAuth/signOut/{sessionId.UrlEncode()}";
        try {
            var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(requestUri).ConfigureAwait(true);
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) {
            _log.LogError("Failed to sign out", e);
            return false;
        }
    }
}
