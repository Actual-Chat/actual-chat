namespace ActualChat.App.Maui.Services;

public class MobileAuthClient
{
    private ClientAppSettings AppSettings { get; }
    private HttpClient HttpClient { get; }
    private ILogger<MobileAuthClient> Log { get; }

    public MobileAuthClient(
        ClientAppSettings clientAppSettings,
        HttpClient httpClient,
        ILogger<MobileAuthClient> log)
    {
        AppSettings = clientAppSettings;
        HttpClient = httpClient;
        Log = log;
    }

    public async Task<bool> SetupSession()
    {
        try {
            var session = await AppSettings.WhenSessionReady.ConfigureAwait(false);
            var sessionId = session.Id.Value;
            var requestUri = $"{AppSettings.BaseUrl}mobileAuth/setupSession/{sessionId.UrlEncode()}";
            var response = await HttpClient.GetAsync(requestUri).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to setup session");
            return false;
        }
    }

    public async Task<bool> SignInGoogle(string code)
    {
        if (code.IsNullOrEmpty())
            throw new ArgumentException($"'{nameof(code)}' cannot be null or empty.", nameof(code));

        var session = await AppSettings.WhenSessionReady.ConfigureAwait(false);
        var sessionId = session.Id.Value;
        var requestUri = $"{AppSettings.BaseUrl}mobileAuth/signInGoogleWithCode/{sessionId.UrlEncode()}/{code.UrlEncode()}";
        try {
            var response = await HttpClient.GetAsync(requestUri).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to sign in google");
            return false;
        }
    }

    public async Task<bool> SignOut()
    {
        var session = await AppSettings.WhenSessionReady.ConfigureAwait(false);
        var sessionId = session.Id.Value;
        var requestUri = $"{AppSettings.BaseUrl}mobileAuth/signOut/{sessionId.UrlEncode()}";
        try {
            var response = await HttpClient.GetAsync(requestUri).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to sign out");
            return false;
        }
    }
}
