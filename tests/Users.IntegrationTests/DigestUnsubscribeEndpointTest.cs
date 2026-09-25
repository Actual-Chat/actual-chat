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
    private EmailMeterTap Meters { get; } = new();

    protected override async Task DisposeAsync()
    {
        Meters.Dispose();
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task GetShouldTurnDigestOff()
    {
        // arrange
        var account = await Tester.SignInAsNew("Alice",
            a => a.WithEmailIdentity(ActualChat.Email.Parse("alice-digest@example.com")));
        using var http = AppHost.NewHttpClient();
        var offCount = CountSubscriptions("off", "link_get");

        // act
        var response = await http.GetAsync(DigestEmailEndpointExt.GetUnsubscribePath(Tokens.Create(account.Id)));
        var html = await response.Content.ReadAsStringAsync();

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        html.Should().Contain("Digest emails are off");
        html.Should().Contain("alice-digest@example.com", "the page names the account it acted on");
        html.Should().NotContain("signed in as", "no session cookie means no other account to warn about");
        html.Should().Contain("/resubscribe", "the page offers an Undo");
        var settings = await KvasBackend.ForUser(account.Id).UserEmailsSettings().Get();
        settings.IsDigestEnabled.Should().BeFalse();
        CountSubscriptions("off", "link_get").Should().Be(offCount + 1, "the footer link is a GET");
    }

    [Fact]
    public async Task PageShouldWarnWhenSignedInAsAnotherAccount()
    {
        // arrange
        var ownAccount = await Tester.SignInAsNew("Grace");
        await using var otherTester = AppHost.NewWebClientTester(Out);
        var otherAccount = await otherTester.SignInAsNew("Heidi");
        using var http = NewHttpClientSignedInAs(Tester.Session);

        // act
        var response = await http.GetAsync(DigestEmailEndpointExt.GetUnsubscribePath(Tokens.Create(otherAccount.Id)));
        var html = await response.Content.ReadAsStringAsync();

        // assert
        html.Should().Contain(otherAccount.Name, "the page names the account the email went to");
        html.Should().Contain("signed in as").And.Contain(ownAccount.Name,
            "the reader must learn their own Settings are not the ones that changed");
        var ownSettings = await KvasBackend.ForUser(ownAccount.Id).UserEmailsSettings().Get();
        ownSettings.IsDigestEnabled.Should().BeTrue("the link acts on the token's account only");
    }

    [Fact]
    public async Task PageShouldNotWarnWhenSignedInAsTheSameAccount()
    {
        // arrange
        var account = await Tester.SignInAsNew("Ivan");
        using var http = NewHttpClientSignedInAs(Tester.Session);

        // act
        var response = await http.GetAsync(DigestEmailEndpointExt.GetUnsubscribePath(Tokens.Create(account.Id)));
        var html = await response.Content.ReadAsStringAsync();

        // assert
        html.Should().Contain(account.Name);
        html.Should().NotContain("signed in as");
    }

    [Fact]
    public async Task RepeatedUnsubscribeShouldCountOnce()
    {
        // arrange
        var account = await Tester.SignInAsNew("Dave");
        using var http = AppHost.NewHttpClient();
        var path = DigestEmailEndpointExt.GetUnsubscribePath(Tokens.Create(account.Id));
        var offCount = CountSubscriptions("off", "link_get");

        // act
        await http.GetAsync(path);
        await http.GetAsync(path);

        // assert
        CountSubscriptions("off", "link_get").Should().Be(offCount + 1,
            "a link scanner re-opening the link is not another opt-out");
    }

    [Fact]
    public async Task PostShouldTurnDigestOff()
    {
        // arrange
        var account = await Tester.SignInAsNew("Bob");
        using var http = AppHost.NewHttpClient();

        var offCount = CountSubscriptions("off", "link_post");

        // act
        var response = await http.PostAsync(DigestEmailEndpointExt.GetUnsubscribePath(Tokens.Create(account.Id)), null);

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, "RFC 8058 one-click POST must work without a body");
        var settings = await KvasBackend.ForUser(account.Id).UserEmailsSettings().Get();
        settings.IsDigestEnabled.Should().BeFalse();
        CountSubscriptions("off", "link_post").Should().Be(offCount + 1,
            "a mail client's one-click unsubscribe is a POST");
    }

    [Fact]
    public async Task ResubscribeShouldTurnDigestBackOn()
    {
        // arrange
        var account = await Tester.SignInAsNew("Carol");
        using var http = AppHost.NewHttpClient();
        var token = Tokens.Create(account.Id);
        await http.GetAsync(DigestEmailEndpointExt.GetUnsubscribePath(token));
        var onCount = CountSubscriptions("on", "link_post");

        // act
        var response = await http.PostAsync($"/emails/digest/{token}/resubscribe", null);
        var html = await response.Content.ReadAsStringAsync();

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Digest emails are on again");
        var settings = await KvasBackend.ForUser(account.Id).UserEmailsSettings().Get();
        settings.IsDigestEnabled.Should().BeTrue();
        CountSubscriptions("on", "link_post").Should().Be(onCount + 1);
    }

    [Fact]
    public async Task SettingsToggleShouldCountAsSettings()
    {
        // arrange
        await Tester.SignInAsNew("Erin");
        var offCount = CountSubscriptions("off", "settings");
        var onCount = CountSubscriptions("on", "settings");

        // act
        await SetDigestEnabled(false);
        await SetDigestEnabled(false);
        await SetDigestEnabled(true);

        // assert
        CountSubscriptions("off", "settings").Should().Be(offCount + 1, "saving the same value again is not a toggle");
        CountSubscriptions("on", "settings").Should().Be(onCount + 1);
    }

    [Fact]
    public async Task UnsubscribeShouldUpdateSettingsUI()
    {
        // arrange
        var account = await Tester.SignInAsNew("Frank");
        using var http = AppHost.NewHttpClient();
        var settingsUI = Tester.AppServices.UserSettingsUI(Tester.Session);
        var computed = await Computed.Capture(
            () => settingsUI.UserSettings.Get(Tester.Session, nameof(UserEmailsSettings)));
        ((computed.Value as UserEmailsSettings)?.IsDigestEnabled ?? true).Should().BeTrue();

        // act
        await http.GetAsync(DigestEmailEndpointExt.GetUnsubscribePath(Tokens.Create(account.Id)));

        // assert
        await TestWait.When(async innerCt => {
            computed.IsInvalidated().Should().BeTrue("the settings the UI shows must react to the link");
            var settings = await settingsUI.UserEmailsSettings().Get(innerCt);
            settings.IsDigestEnabled.Should().BeFalse();
        }, TimeSpan.FromSeconds(10));
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

    // Private methods

    private HttpClient NewHttpClientSignedInAs(Session session)
    {
        var http = AppHost.NewHttpClient();
        http.DefaultRequestHeaders.Add("Cookie", $"{Constants.Session.CookieName}={session.Id}");
        return http;
    }

    private Task SetDigestEnabled(bool isEnabled)
        => Tester.Commander.Call(new UserSettings_Set {
            Session = Tester.Session,
            Key = nameof(UserEmailsSettings),
            Value = new UserEmailsSettings { IsDigestEnabled = isEnabled },
        });

    private int CountSubscriptions(string action, string source)
        => Meters.Count(EmailMeters.DigestSubscriptions, ("action", action), ("source", source));
}
