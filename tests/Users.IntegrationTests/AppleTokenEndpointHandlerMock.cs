using System.Net;
using System.Text;

namespace ActualChat.Users.IntegrationTests;

/// <summary>
/// Simulates Apple's token endpoint, returning an id_token JWT keyed by authorization code.
/// Thread-safe: each test registers its own code → (sub, email) mapping via <see cref="Setup"/>.
/// </summary>
public sealed class AppleTokenEndpointHandlerMock : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, (string Sub, string Email)> _codes = new();

    public string Setup(string sub, string email)
    {
        var code = UniqueNames.Prefix();
        _codes[code] = (sub, email);
        return code;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var code = ParseFormValue(body, "code");

        if (code == null || !_codes.TryRemove(code, out var claims))
            return new HttpResponseMessage(HttpStatusCode.BadRequest) {
                Content = new StringContent("unknown authorization code"),
            };

        var header = Convert.ToBase64String("""{"alg":"none","typ":"JWT"}"""u8)
            .TrimEnd('=');
        var payload = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    $$"""{"sub":"{{claims.Sub}}","email":"{{claims.Email}}","email_verified":"true"}"""))
            .TrimEnd('=');
        var idToken = $"{header}.{payload}.";

        var json = $$"""
            {
                "access_token": "mock_access_token",
                "token_type": "bearer",
                "expires_in": 3600,
                "id_token": "{{idToken}}"
            }
            """;

        return new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static string? ParseFormValue(string formBody, string key)
    {
        foreach (var pair in formBody.Split('&')) {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex < 0)
                continue;
            if (Uri.UnescapeDataString(pair[..eqIndex]) == key)
                return Uri.UnescapeDataString(pair[(eqIndex + 1)..]);
        }
        return null;
    }
}
