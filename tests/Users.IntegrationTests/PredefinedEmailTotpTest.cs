using ActualChat.Testing.Host;
using ActualChat.Users.Email;
using ActualChat.Users.Module;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class PredefinedEmailTotpTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private const int PredefinedTotp = 123123;
    private UsersSettings Settings => AppHost.Services.GetRequiredService<UsersSettings>();

    [Fact]
    public async Task PredefinedCodeShouldSignInWithoutASentCode()
    {
        // arrange
        var email = ActualChat.Email.Parse($"npc+{Ulid.NewUlid().ToString().ToLower()}@actual.chat");
        var settings = Settings;
        var oldPredefinedEmailTotps = settings.PredefinedEmailTotps;
        settings.PredefinedEmailTotps = new Dictionary<string, int> { { "npc", PredefinedTotp } };
        var tester = AppHost.NewWebClientTester(Out);
        await using var _ = tester.ConfigureAwait(false);
        var session = tester.Session;

        // act
        bool isWrongAccepted;
        bool isPredefinedAccepted;
        AccountFull account;
        try {
            await tester.Commander.Call(new EmailAuth_SendTotp { Session = session, Email = email });
            isWrongAccepted = await tester.Commander
                .Call(new EmailAuth_ValidateTotp { Session = session, Email = email, Totp = 222222 });
            isPredefinedAccepted = await tester.Commander
                .Call(new EmailAuth_ValidateTotp { Session = session, Email = email, Totp = PredefinedTotp });
            await AppHost.ConfirmPendingRegistration(session);
            account = await tester.Accounts.GetOwn(session, default);
        }
        finally {
            settings.PredefinedEmailTotps = oldPredefinedEmailTotps;
        }

        // assert
        isWrongAccepted.Should().BeFalse();
        isPredefinedAccepted.Should().BeTrue();
        account.Email.Should().Be(email.Value);
        account.IsAdmin.Should().BeFalse("a predefined code is shared, even though the email is a team one");
    }

    [Theory]
    [InlineData("npc+promo@actual.chat", "npc")]
    [InlineData("NPC+Promo@Actual.Chat", "npc")]
    [InlineData("npc+a+b@actual.chat", "npc")]
    [InlineData("npc@actual.chat", null)]
    [InlineData("npc+@actual.chat", null)]
    [InlineData("+promo@actual.chat", null)]
    [InlineData("npcx+promo@actual.chat", null)]
    [InlineData("xnpc+promo@actual.chat", null)]
    [InlineData("npc+promo@gmail.com", null)]
    [InlineData("npc+promo@notactual.chat", null)]
    [InlineData("npc+promo@sub.actual.chat", null)]
    public void PrefixShouldMatchOnlySuffixedTeamEmails(string email, string? expectedPrefix)
    {
        // arrange
        var predefinedEmailTotps = new Dictionary<string, int> { { "npc", PredefinedTotp } };

        // act
        var prefix = EmailAuth.GetPredefinedTotpPrefix(predefinedEmailTotps, email);

        // assert
        prefix.Should().Be(expectedPrefix);
    }

    [Theory]
    [InlineData("https://voxt.ai", false)]
    [InlineData("https://preview.example.com", false)]
    [InlineData("https://dev.voxt.ai", true)]
    [InlineData("https://local.voxt.ai", true)]
    [InlineData("https://wt1.local.voxt.ai", true)]
    public void PredefinedCodeShouldNeverWorkOnProduction(string baseUrl, bool expectedIsAllowed)
    {
        // arrange
        var hostInfo = new HostInfo {
            BaseUrl = baseUrl,
            IsTested = false,
        };

        // act
        var isAllowed = EmailAuth.IsPredefinedTotpHost(hostInfo);

        // assert
        isAllowed.Should().Be(expectedIsAllowed);
    }

    [Fact]
    public void PredefinedEmailShouldNeverBeAdmin()
    {
        // arrange
        var email = ActualChat.Email.Parse("npc+promo@actual.chat");
        var account = new AccountFull("").WithEmailIdentity(email);
        var predefinedTotps = ImmutableDictionary<string, int>.Empty;
        var predefinedEmailTotps = new Dictionary<string, int> { { "npc", PredefinedTotp } };

        // act
        var isAdmin = AccountsBackend.IsAdmin(account, true, predefinedTotps, predefinedEmailTotps);
        var isAdminWithoutSetting = AccountsBackend.IsAdmin(account, true, predefinedTotps, predefinedTotps);

        // assert
        isAdmin.Should().BeFalse();
        isAdminWithoutSetting.Should().BeTrue("without the setting it's a regular team email");
    }
}
