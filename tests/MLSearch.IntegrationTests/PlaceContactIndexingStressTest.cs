using ActualChat.Search;
using ActualChat.Testing.Host;

namespace ActualChat.MLSearch.IntegrationTests;

[Collection(nameof(SlowMLSearchCollection))]
[Trait("Category", "Slow")]
public class PlaceContactIndexingStressTest(SlowAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<SlowAppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);

    private string IsolationKey { get; } = UniqueNames.Random();

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Theory]
    [InlineData(77)]
    [InlineData(1_000)]
    public async Task ShouldIndexManyPlaces(int portionSize)
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var portion1 = await CreatePlaces(portionSize, "The first portion:");
        var portion2 = await CreatePlaces(50, "The second portion:");

        // act
        var searchResults = await Find("first");

        // assert
        searchResults.Select(x => x.Text)
            .Should()
            .OnlyHaveUniqueItems()
            .And.BeSubsetOf(portion1.Select(x => x.Title));

        // act
        searchResults = await Find("second");

        // assert
        searchResults.Select(x => x.Text)
            .Should()
            .OnlyHaveUniqueItems()
            .And.BeSubsetOf(portion2.Select(x => x.Title));
    }

    // Private methods

    private Task<Place[]> CreatePlaces(int count, string prefix)
        => Enumerable.Range(1, count).Select(i => CreatePlace($"{prefix} {i}")).Collect(Environment.ProcessorCount / 2);

    private Task<Place> CreatePlace(string title)
        => Tester.CreatePlace(false, $"{title} {IsolationKey}");

    private async Task<FoundContact[]> Find(string criteria, int expected = 50)
    {
        FoundContact[] results = [];
        await TestExt.When(async () => {
                results = await Tester.FindPlaces($"{IsolationKey} {criteria}", true, expected);
                results.Should().HaveCount(expected, "for criteria '{0}'", criteria);
            },
            TestRunnerInfo.IsBuildAgent() ? TimeSpan.FromSeconds(90) : TimeSpan.FromSeconds(30));
        return results;
    }
}
