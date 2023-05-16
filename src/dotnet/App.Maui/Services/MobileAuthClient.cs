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

    public async Task<string> GetOrCreateSessionId()
    {
        try {
            var requestUri = $"{AppSettings.BaseUrl}mobileAuth/getOrCreateSession";
            var response = await HttpClient.GetAsync(requestUri).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var sessionId = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return sessionId;
        }
        catch (Exception e) {
            Log.LogError(e, "GetOrCreateSessionId failed - probably server is unreachable");
            throw;
        }
    }

    public async Task<bool> SignInGoogle(string code)
    {
        if (code.IsNullOrEmpty())
            throw new ArgumentException($"'{nameof(code)}' cannot be null or empty.", nameof(code));

        var session = await MauiSessionProvider.GetSession().ConfigureAwait(false);
        var sessionId = session.Id.Value;
        var requestUri = $"{AppSettings.BaseUrl}mobileAuth/signInGoogleWithCode/{sessionId.UrlEncode()}/{code.UrlEncode()}";
        try {
            var response = await HttpClient.GetAsync(requestUri).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) {
            Log.LogError(e, "SignInGoogle failed");
            return false;
        }
    }

    public async Task SignInGoogle()
    {
        var session = await MauiSessionProvider.GetSession().ConfigureAwait(false);
        var sessionId = session.Id.Value;
        var requestUri = $"{AppSettings.BaseUrl}mobileAuth/signOut/{sessionId.UrlEncode()}";
        try {
            var response = await HttpClient.GetAsync(requestUri).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception e) {
            Log.LogError(e, "SignInGoogle failed");
            throw;
        }
    }
}
