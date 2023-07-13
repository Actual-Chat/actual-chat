namespace ActualChat.App.Maui.Services;

public sealed class MobileAuthClient
{
    private HttpClient? _httpClient;
    private ILogger? _log;

    private IServiceProvider Services { get; }
    private HttpClient HttpClient => _httpClient ??= Services.GetRequiredService<HttpClient>();
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public MobileAuthClient(IServiceProvider services)
        => Services = services;

    public async Task<bool> SignInAppleWithCode(string code, string name, string email, string userId)
    {
        try {
            var values = new List<KeyValuePair<string, string>> {
                new ("SessionId", Services.Session().Id.Value),
                new ("Name", name),
                new ("Email", email),
                new ("Code", code),
                new ("UserId", userId),
            };
            var requestUri = $"{MauiSettings.BaseUrl}mobileAuth/signInAppleWithCode";
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

    public async Task<bool> SignInGoogleWithCode(string code)
    {
        if (code.IsNullOrEmpty())
            throw new ArgumentException($"'{nameof(code)}' cannot be null or empty.", nameof(code));

        var sessionId = Services.Session().Id.Value;
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
