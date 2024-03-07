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
    [InlineData(10, 3)]
    [InlineData(55, 7)]
    public async Task ShouldListBatches(int count, int batchSize)
    {
        // arrange
        await Tester.SignInAsAlice();
        var lastChanged = await Sut.GetLastChanged(CancellationToken.None);
        var minVersion = lastChanged?.Version ?? 0;
        var accounts = await Tester.CreateAccounts(count);

        // act
        await TestExt.WhenMetAsync(async () => {
                log.LogInformation("Selecting account batches minVersion={MinVersion}(#{LastId})]",
                    minVersion,
                    lastChanged?.Id);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var cancellationToken = cts.Token;
                var retrieved = await Sut.BatchChanged(minVersion,
                        long.MaxValue,
                        lastChanged?.Id ?? UserId.None,
                        batchSize,
                        cancellationToken)
                    .ToApiArrayAsync(cancellationToken)
                    .Flatten();
                Log.LogInformation("{Retrieved}", retrieved);
                retrieved.Select(x => x.User.Name).Should().Contain(accounts.Select(x => x.User.Name));
            },
            TimeSpan.FromSeconds(20));
    }
}
