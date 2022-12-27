namespace ActualChat.App.Maui.Services;

public class MobileAuthClient
{
    private ClientAppSettings AppSettings { get; }
    private UrlMapper UrlMapper { get; }
    private ILogger<MobileAuthClient> Log { get; }

    public MobileAuthClient(IServiceProvider services)
    {
        AppSettings = services.GetRequiredService<ClientAppSettings>();
        UrlMapper = services.GetRequiredService<UrlMapper>();
        Log = services.GetRequiredService<ILogger<MobileAuthClient>>();
    }

    public async Task<bool> SetupSession()
    {
        var sessionId = AppSettings.SessionId;
        var requestUri = $"{UrlMapper.BaseUrl}mobileAuth/setupSession/{sessionId.UrlEncode()}";
        try {
            var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(requestUri).ConfigureAwait(true);
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to setup session");
            return false;
        }
    }

    public async Task<bool> SignInGoogle(string code)
    {
        if (string.IsNullOrEmpty(code))
            throw new ArgumentException($"'{nameof(code)}' cannot be null or empty.", nameof(code));
        var sessionId = AppSettings.SessionId;
        var requestUri = $"{UrlMapper.BaseUrl}mobileAuth/signInGoogleWithCode/{sessionId.UrlEncode()}/{code.UrlEncode()}";
        try {
            var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(requestUri).ConfigureAwait(true);
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to sign in google");
            return false;
        }
    }

    public async Task<bool> SignOut()
    {
        var sessionId = AppSettings.SessionId;
        var requestUri = $"{UrlMapper.BaseUrl}mobileAuth/signOut/{sessionId.UrlEncode()}";
        try {
            var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(requestUri).ConfigureAwait(true);
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to sign out");
            return false;
        }
    }
}
