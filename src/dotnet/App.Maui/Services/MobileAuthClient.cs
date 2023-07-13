namespace ActualChat.App.Maui.Services;

public sealed class MobileAuthClient
{
    private HttpClient HttpClient { get; }
    private ISessionResolver SessionResolver { get; }
    private ILogger Log { get; }

    public MobileAuthClient(IServiceProvider services)
    {
        HttpClient = services.GetRequiredService<HttpClient>();
        SessionResolver = services.GetRequiredService<ISessionResolver>();
        Log = services.GetRequiredService<ILogger<MobileAuthClient>>();
    }

    public async Task SignOut()
    {
        var session = await SessionResolver.GetSession(CancellationToken.None).ConfigureAwait(false);
        var sessionId = session.Id.Value;
        var requestUri = $"{MauiSettings.BaseUrl}mobileAuth/signOut/{sessionId.UrlEncode()}";
        try {
            var response = await HttpClient.GetAsync(requestUri).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception e) {
            Log.LogError(e, "SignOut failed");
            throw;
        }
    }

    public async Task<bool> SignInApple(string code, string name, string email, string userId)
    {
        var session = await SessionResolver.GetSession(CancellationToken.None).ConfigureAwait(false);
        var requestUri = $"{MauiSettings.BaseUrl}mobileAuth/signInAppleWithCode";
        try {
            var values = new List<KeyValuePair<string, string>> {
                new ("SessionId", session.Id.Value),
                new ("Name", name),
                new ("Email", email),
                new ("Code", code),
                new ("UserId", userId),
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

        var session = await SessionResolver.GetSession(CancellationToken.None).ConfigureAwait(false);
        var sessionId = session.Id.Value;
        var requestUri = $"{MauiSettings.BaseUrl}mobileAuth/signInGoogleWithCode/{sessionId.UrlEncode()}/{code.UrlEncode()}";
        try {
            var response = await HttpClient.GetAsync(requestUri).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) {
            Log.LogError(e, "SignInGoogle failed");
            return false;
        }
    }
}
