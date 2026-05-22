using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class AccountListingTest(AppHostFixture fixture, ITestOutputHelper @out, ILogger<AccountListingTest> log)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out, log)
{
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private IAccountsBackend Sut { get; } = fixture.AppHost.Services.GetRequiredService<IAccountsBackend>();
    private string IsolationKey { get; } = UniqueNames.Random();

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Theory]
    [InlineData(30, 15)]
    [InlineData(150, 29)]
    public async Task ShouldListChanged(int count, int batchSize)
    {
        // arrange
        var alice = await Tester.SignInAsNew("Alice");
        var accounts = await Tester.CreateAccounts(count, nameFactory: _ => $"{IsolationKey} User");

        // act
        var minVersion = alice.Version;
        var lastChangedId = alice.Id;
        var allRetrieved = new List<AccountFull>();
        while (true) {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var cancellationToken = cts.Token;
            var retrieved = await Sut.ListChangedFull(minVersion,
                long.MaxValue,
                lastChangedId,
                batchSize,
                cancellationToken);
            if (retrieved.Length == 0)
                break;

            minVersion = retrieved[^1].Version;
            lastChangedId = retrieved[^1].Id;
            allRetrieved.AddRange(retrieved);
        }

        // assert
        allRetrieved.Select(x => x.Name).Should().Contain(accounts.Select(x => x.Name));
    }
}
