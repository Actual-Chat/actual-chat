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
}
