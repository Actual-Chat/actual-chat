namespace ActualChat.App.Maui.Services;

public sealed class MobileAuthClient
{
    private HttpClient HttpClient { get; }
    private ILogger<MobileAuthClient> Log { get; }

    public MobileAuthClient(HttpClient httpClient, ILogger<MobileAuthClient> log)
    {
        HttpClient = httpClient;
        Log = log;
    }

    public async Task SetupSession(Session session)
    {
        try {
            var sessionId = session.Id.Value;
            var requestUri = $"{AppSettings.BaseUrl}mobileAuth/setupSession/{sessionId.UrlEncode()}";
            var response = await HttpClient.GetAsync(requestUri).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception e) {
            Log.LogError(e, "Session setup failed - probably server is unreachable");
            throw;
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
            Log.LogError(e, "Google sign-in failed");
            return false;
        }
    }

    public async Task SignOut()
    {
        var session = await AppSettings.WhenSessionReady.ConfigureAwait(false);
        var sessionId = session.Id.Value;
        var requestUri = $"{AppSettings.BaseUrl}mobileAuth/signOut/{sessionId.UrlEncode()}";
        try {
            var response = await HttpClient.GetAsync(requestUri).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception e) {
            Log.LogError(e, "Sign-out failed");
            throw;
        }
    }
}
