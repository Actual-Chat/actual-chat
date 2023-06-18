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

    public async Task<string> GetOrCreateSessionId(string? sessionId = null)
    {
        try {
            var requestUri = $"{AppSettings.BaseUrl}mobileAuth/getOrCreateSession/{sessionId}";
            var response = await HttpClient.GetAsync(requestUri).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "GetOrCreateSessionId failed - probably server is unreachable");
            throw;
        }
    }

    public async Task<bool> SignInApple(string code, string name, string email)
    {
        var session = await MauiSessionProvider.GetSession().ConfigureAwait(false);
        var requestUri = $"{AppSettings.BaseUrl}mobileAuth/signInAppleWithCode";
        try {
            var values = new List<KeyValuePair<string, string>> {
                new ("SessionId", session.Id.Value),
                new ("Name", name),
                new ("Email", email),
                new ("Code", code),
            };
            var request = new HttpRequestMessage(HttpMethod.Post, requestUri) {
                Content = new FormUrlEncodedContent(values),
            };
            var response = await HttpClient.SendAsync(request).ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (Exception e) {
            Log.LogError(e, "SignInApple failed");

            return false;
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

    public async Task SignOut()
    {
        var session = await MauiSessionProvider.GetSession().ConfigureAwait(false);
        var sessionId = session.Id.Value;
        var requestUri = $"{AppSettings.BaseUrl}mobileAuth/signOut/{sessionId.UrlEncode()}";
        try {
            var response = await HttpClient.GetAsync(requestUri).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception e) {
            Log.LogError(e, "SignOut failed");
            throw;
        }
    }
}
