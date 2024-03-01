using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class AccountListingTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;
    private IAccountsBackend _sut = null!;
    private RandomSymbolGenerator _rsg = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _tester = AppHost.NewWebClientTester(Out);
        _sut = AppHost.Services.GetRequiredService<IAccountsBackend>();
        _rsg = new RandomSymbolGenerator(length: 5, alphabet: Alphabet.AlphaNumeric);
    }

    protected override async Task DisposeAsync()
        => await _tester.DisposeAsync().AsTask();

    [Theory]
    [InlineData(10, 3)]
    [InlineData(55, 7)]
    public async Task ShouldListBatches(int count, int batchSize)
    {
        // arrange
        var suffix = _rsg.Next();
        var lastChanged = await _sut.GetLastChanged(CancellationToken.None);
        var accounts = await CreateAccounts(count, i => $"User_{i}_{suffix}");

        // act
        var i = 0;
        var batches = _sut.BatchChanged(lastChanged?.Version ?? 0,
            lastChanged != null ? ApiSet.New(lastChanged.Id) : ApiSet<UserId>.Empty,
            batchSize,
            CancellationToken.None);
        var uniqueListedIds = new HashSet<UserId>();
        await foreach (var batch in batches) {
            // assert
            foreach (var account in batch)
                accounts.Should().ContainKey(account.Id, $"i={i}");
            uniqueListedIds.AddRange(batch.Select(x => x.Id));
        }
        uniqueListedIds.Should().HaveCount(count);
    }

    private async Task<Dictionary<UserId, AccountFull>> CreateAccounts(int count, Func<int,string> userNameFactory)
    {
        var accounts = await _tester.CreateAccounts(count, userNameFactory);
        return accounts.ToDictionary(x => x.Id);
    }
}
