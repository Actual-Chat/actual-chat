using System.Net;
using ActualChat.Testing.Host;

namespace ActualChat.OAuth.IntegrationTests;

public abstract class OAuthTestBase<TFixture>(TFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<TFixture>(fixture, @out)
    where TFixture : AppHostFixture
{
    protected WebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    protected Uri BaseUri => Tester.UrlMapper.BaseUri;
    protected HttpClient Http => field ??= new(new HttpClientHandler {
        AllowAutoRedirect = false,
        UseCookies = false,
    }) {
        BaseAddress = BaseUri,
    };

    protected override async Task DisposeAsync()
    {
        Http.Dispose();
        await Tester.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    protected async Task<JsonElement> GetJson(string path)
    {
        var response = await Http.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    protected async Task<string> RegisterClient(params string[] redirectUris)
    {
        var (_, doc) = await Register(new {
            client_name = "Test Client",
            redirect_uris = redirectUris,
            grant_types = new[] { "authorization_code", "refresh_token" },
            token_endpoint_auth_method = "none",
        });
        return doc.GetProperty("client_id").GetString()!;
    }

    protected async Task<(HttpStatusCode, JsonElement)> Register(object body)
    {
        var response = await Http.PostAsJsonAsync("/oauth/register", body);
        var json = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(json).RootElement);
    }
}
