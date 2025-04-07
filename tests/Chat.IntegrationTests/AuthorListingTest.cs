using ActualChat.Testing.Host;
using ActualChat.Users;
using ActualLab.Versioning;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class AuthorListingTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatCollection.AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private IAuthorsBackend AuthorsBackend { get; } = fixture.AppHost.Services.GetRequiredService<IAuthorsBackend>();
    private VersionGenerator<long> VersionGenerator { get; } = fixture.AppHost.Services.VersionGenerator<long>();

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 2)]
    [InlineData(2, 2, 2)]
    [InlineData(100, 20, 99)]
    public async Task ShouldListChangedPlaceAuthors(int placeCount, int memberCount, int batchSize)
    {
        // arrange
        var minVersion = VersionGenerator.NextVersion();
        await Tester.SignInAsUniqueAlice();
        var accounts = await Tester.CreateAccounts(memberCount);
        var places = await CreatePlaces(placeCount, accounts);

        // act
        var lastChangedId = AuthorId.None;
        var allRetrieved = new List<AuthorFull>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;
        while (true) {
            var retrieved = await AuthorsBackend.ListChanged(new ChangedAuthorsQuery {
                    MinVersion = minVersion,
                    MaxVersion = long.MaxValue,
                    LastId = lastChangedId,
                    IsPlaceAuthor = true,
                    Limit = batchSize,
                },
                cancellationToken);
            if (retrieved.Length == 0)
                break;

            minVersion = retrieved[^1].Version;
            lastChangedId = retrieved[^1].Id;
            allRetrieved.AddRange(retrieved);
        }

        // assert
        allRetrieved.Should().OnlyContain(x => x.IsPlaceAuthor);
        allRetrieved.Select(x => x.UserId).Should().Contain(accounts.Select(x => x.Id));
        allRetrieved.Select(x => x.ChatId.PlaceId).Should().Contain(places.Select(x => x.Id));
    }

    private Task<Place[]> CreatePlaces(int placeCount, AccountFull[] members)
        => Enumerable.Range(1, placeCount)
            .Select(i => Tester.CreatePlace(i % 2 == 0, UniqueNames.Place(i), members))
            .Collect(Environment.ProcessorCount);
}
