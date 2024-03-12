using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class AccountListingTest(AppHostFixture fixture, ITestOutputHelper @out, ILogger<AccountListingTest> log)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out, log)
{
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private IAccountsBackend Sut { get; } = fixture.AppHost.Services.GetRequiredService<IAccountsBackend>();

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Theory]
    [InlineData(30, 15)]
    [InlineData(150, 29)]
    public async Task ShouldListBatches(int count, int batchSize)
    {
        // arrange
        var alice = await Tester.SignInAsNew("Alice");
        var minVersion = alice.Version;
        var lastChangedId = alice.Id;
        var accounts = await Tester.CreateAccounts(count);

        // act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;
        var retrieved = await Sut.BatchChanged(minVersion,
                long.MaxValue,
                lastChangedId,
                batchSize,
                cancellationToken)
            .ToApiArrayAsync(cancellationToken)
            .Flatten();

        // assert
        retrieved.Select(x => x.User.Name).Should().Contain(accounts.Select(x => x.User.Name));
    }
}
