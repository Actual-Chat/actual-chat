using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class AccountListingTest(AppHostFixture fixture, ITestOutputHelper @out, ILogger<AccountListingTest> log)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out, log)
{
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private IAccountsBackend Sut { get; } = fixture.AppHost.Services.GetRequiredService<IAccountsBackend>();
    private RandomSymbolGenerator RandomSymbolGenerator { get; } = new RandomSymbolGenerator(length: 5, alphabet: Alphabet.AlphaNumeric);

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
        var suffix = RandomSymbolGenerator.Next();
        var lastChanged = await Sut.GetLastChanged(CancellationToken.None);
        var accounts = await Tester.CreateAccounts(count, i => $"User_{i}_{suffix}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var cancellationToken = cts.Token;

        // act
        var minVersion = lastChanged?.Version ?? 0;
        log.LogInformation("Selecting account batches minVersion={MinVersion}(#{LastId})]",
            minVersion,
            lastChanged?.Id);
        var retrieved = await Sut.BatchChanged(minVersion,
                long.MaxValue,
                lastChanged?.Id ?? UserId.None,
                batchSize,
                cancellationToken)
            .ToApiArrayAsync(cancellationToken)
            .Flatten();
        Log.LogInformation("{Retrieved}", retrieved);
        retrieved.Select(x => x.FullName)
            .Distinct()
            .OrderBy(x => x)
            .Should()
            .Equal(accounts.Select(x => x.FullName).OrderBy(x => x));
        retrieved.DistinctBy(x => x.Id).Should().BeEquivalentTo(accounts, o => o.IdName());
    }
}
