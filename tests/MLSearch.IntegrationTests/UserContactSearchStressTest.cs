using ActualChat.MLSearch.Indexing;
using ActualChat.Search;
using ActualChat.Testing.Host;

namespace ActualChat.MLSearch.IntegrationTests;

[Collection(nameof(MLSearchCollection))]
[Trait("Category", "Slow")]
public class UserContactSearchStressTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;
    private ICommander _commander = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        _tester = AppHost.NewWebClientTester(Out);
        _commander = AppHost.Services.Commander();
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Theory]
    [InlineData(100, 30)]
    [InlineData(300, 500)]
    [InlineData(1_000, 1_000)]
    [InlineData(1_000, 10_000)]
    public async Task ShouldFind(int accountCount, int placeCount)
    {
        // arrange
        var accounts = await _tester.CreateAccounts(accountCount);
        await _tester.SignInAsBob();
        var place = await _tester.CreatePlace(false);

        // act
        foreach (var batch in accounts.Chunk(ContactIndexer.SyncBatchSize)) {
            var placeIds = GeneratePlaceIds()
                .Select(_ => new PlaceId(Generate.Option))
                .Concat([place.Id])
                .Concat(GeneratePlaceIds())
                .ToApiArray();
            var updates = batch.Select(x => x.ToIndexedUserContact(placeIds)).ToApiArray();
            await _commander.Call(new SearchBackend_UserContactBulkIndex(updates, []));
        }
        await _commander.Call(new SearchBackend_Refresh(true));
        var searchResults = await _tester.FindPeople("User", false, place.Id);

        // assert
        searchResults
            .DistinctBy(x => x.SearchMatch.Text)
            .Should()
            .NotBeEmpty();
        return;

        IEnumerable<PlaceId> GeneratePlaceIds()
            => Enumerable.Range(1, placeCount / 2).Select(_ => new PlaceId(Generate.Option));
    }

    // Private methods

}
