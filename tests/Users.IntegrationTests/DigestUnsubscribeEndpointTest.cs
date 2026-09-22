using System.Net;
using ActualChat.Testing.Host;
using ActualChat.Users.Email;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class DigestUnsubscribeEndpointTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private DigestUnsubscribeTokens Tokens { get; }
        = fixture.AppHost.Services.GetRequiredService<DigestUnsubscribeTokens>();
    private IServerKvasBackend KvasBackend { get; }
        = fixture.AppHost.Services.GetRequiredService<IServerKvasBackend>();

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task GetShouldTurnDigestOff()
    {
        // arrange
        var account = await Tester.SignInAsNew("Alice");
        using var http = AppHost.NewHttpClient();

        // act
        var response = await http.GetAsync(DigestEmailEndpointExt.GetUnsubscribePath(Tokens.Create(account.Id)));
        var html = await response.Content.ReadAsStringAsync();

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        html.Should().Contain("Digest emails are off");
        html.Should().Contain("/resubscribe", "the page offers an Undo");
        var settings = await KvasBackend.ForUser(account.Id).UserEmailsSettings().Get();
        settings.IsDigestEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task PostShouldTurnDigestOff()
    {
        // arrange
        var account = await Tester.SignInAsNew("Bob");
        using var http = AppHost.NewHttpClient();

        // act
        var response = await http.PostAsync(DigestEmailEndpointExt.GetUnsubscribePath(Tokens.Create(account.Id)), null);

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, "RFC 8058 one-click POST must work without a body");
        var settings = await KvasBackend.ForUser(account.Id).UserEmailsSettings().Get();
        settings.IsDigestEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task ResubscribeShouldTurnDigestBackOn()
    {
        // arrange
        var account = await Tester.SignInAsNew("Carol");
        using var http = AppHost.NewHttpClient();
        var token = Tokens.Create(account.Id);
        await http.GetAsync(DigestEmailEndpointExt.GetUnsubscribePath(token));

        // act
        var response = await http.PostAsync($"/emails/digest/{token}/resubscribe", null);
        var html = await response.Content.ReadAsStringAsync();

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Digest emails are on again");
        var settings = await KvasBackend.ForUser(account.Id).UserEmailsSettings().Get();
        settings.IsDigestEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task BadTokenShouldBeNotFound()
    {
        // arrange
        using var http = AppHost.NewHttpClient();

        // act
        var response = await http.GetAsync(DigestEmailEndpointExt.GetUnsubscribePath("not-a-token"));

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
