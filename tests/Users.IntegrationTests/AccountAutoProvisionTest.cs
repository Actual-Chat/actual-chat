using ActualChat.App.Server;
using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class AccountAutoProvisionTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;
    private IAccounts _accounts = null!;
    private AppHost _appHost = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _appHost = await NewAppHost("new-user");
        _tester = _appHost.NewWebClientTester(Out);
        _accounts = _appHost.Services.GetRequiredService<IAccounts>();
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeAsync();
        await _appHost.DisposeAsync();
    }

    [Fact]
    public async Task ShouldCreateAccountForNewUser()
    {
        // arrange
        var user = await _tester.SignInAsBob();

        // act
        var account = await _accounts.GetOwn(_tester.Session, default);

        // assert
        account.Should().NotBeNull();
        account.Id.Should().Be(user.Id);
        account.Status.Should().Be(AccountStatus.Active);
    }

    [Fact]
    public async Task ShouldNotCreateAccountForExistingUser()
    {
        // arrange
        var account = await _tester.SignInAsBob();
        await _tester.SignOut();

        // act
        var account2 = await _tester.SignIn(account);

        // assert
        account2.Should().BeEquivalentTo(account, options => options
            .ExcludingSystemProperties()
            .Excluding(x => x.IsGreetingCompleted));
    }

    [Fact]
    public async Task GeneratedUserIdsAreUnused()
    {
        // arrange
        const int accountCount = 20;

        // act
        var userIds = new List<UserId>();
        for (var i = 0; i < accountCount; i++) {
            var account = await _tester.SignInAsUniqueBob();
            userIds.Add(account.Id);
            await _tester.SignOut();
        }

        // assert
        userIds.Should().AllSatisfy(x => x.IsGuest.Should().BeFalse());
        userIds.Distinct().Should().HaveCount(accountCount);
    }
}
