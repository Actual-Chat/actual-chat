using ActualChat.Chat;
using ActualChat.Search;
using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.MLSearch.IntegrationTests;

[Collection(nameof(MLSearchCollection))]
[Trait("Category", "Slow")]
public class UserContactIndexingStressTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private BlazorTester Tester { get; } = fixture.AppHost.NewBlazorTester(@out);

    private string UniquePart { get; } = UniqueNames.Prefix();

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Theory]
    [InlineData(77, 0)]
    [InlineData(77, 1)]
    [InlineData(77, 10)]
    [InlineData(777, 0)]
    [InlineData(777, 1)]
    [InlineData(777, 5)]
    public async Task ShouldIndexManyAccounts(int portionSize, int placeCount)
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var portion1 = await CreateAccounts(portionSize, "The first portion:");
        var portion2 = await CreateAccounts(50, "The second portion:");
        var places = await CreatePlaces(placeCount, [.. portion1, .. portion2]);

        // act
        var searchResults = await Find("second");

        // assert
        searchResults.Select(x => x.Text)
            .Should()
            .OnlyHaveUniqueItems()
            .And.BeSubsetOf(portion2.Select(x => x.Name));

        // act
        searchResults = await Find("first");

        // assert
        searchResults.Select(x => x.Text)
            .Should()
            .OnlyHaveUniqueItems()
            .And.BeSubsetOf(portion1.Select(x => x.Name));

        if (placeCount > 0) {
            // act
            searchResults = await Find("second", places[^1].Id);

            // assert
            searchResults.Select(x => x.Text)
                .Should()
                .OnlyHaveUniqueItems()
                .And.BeSubsetOf(portion2.Select(x => x.Name));

            // act
            searchResults = await Find("first", places[^1].Id);

            // assert
            searchResults.Select(x => x.Text)
                .Should()
                .OnlyHaveUniqueItems()
                .And.BeSubsetOf(portion1.Select(x => x.Name));
        }
    }

    // Private methods

    private async Task<AccountFull[]> CreateAccounts(int portionSize, string prefix)
        => await Tester.CreateAccounts(portionSize, nameFactory: _ => $"{UniquePart} {prefix}");

    private async Task<Place[]> CreatePlaces(int count, params IReadOnlyCollection<AccountFull> usersToInvite)
    {
        var places = new Place[count];
        for (var i = 0; i < count; i++)
            places[i] = await CreatePlace($"test place {i}", usersToInvite);
        return places;
    }

    private Task<Place> CreatePlace(string title, params IReadOnlyCollection<AccountFull> usersToInvite)
        => Tester.CreatePlace(false, $"{UniquePart} {title}", usersToInvite);

    private Task<ContactSearchResult[]> Find(string criteria, PlaceId? placeId = null, int expected = 50)
        => TestsExt.When(async () => {
                var results = await Tester.FindPeople($"{UniquePart} {criteria}", false, placeId, expected);
                results.Should().HaveCount(expected, "for place #{PlaceId} and criteria '{Criteria}'", placeId, criteria);
                return results;
            },
            Intervals.Fixed(TimeSpan.FromSeconds(0.5)),
            TimeSpan.FromSeconds(TestRunnerInfo.IsBuildAgent() ? 60 : 20));
}
