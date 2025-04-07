using ActualChat.Search;
using ActualChat.Testing.Host;

namespace ActualChat.MLSearch.IntegrationTests;

[Collection(nameof(MLSearchCollection))]
[Trait("Category", "Slow")]
public class GroupContactIndexingStressTest(AppHostFixture fixture, ITestOutputHelper @out)
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
    [InlineData(77)]
    [InlineData(1_000)]
    public async Task ShouldIndexManyGroups(int portionSize)
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var portion1 = await CreateGroups(portionSize, "The first portion:");
        var portion2 = await CreateGroups(50, "The second portion:");

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

    private Task<Chat.Chat[]> CreateGroups(int count, string prefix)
        => Enumerable.Range(1, count).Select(i => CreateGroup($"{prefix} {i}")).Collect(Environment.ProcessorCount);

    private async Task<Chat.Chat> CreateGroup(string title)
    {
        var (chat, _) = await Tester.CreateAndGetChat(false, $"{title} {UniquePart}");
        return chat;
    }

    private async Task<ContactSearchResult[]> Find(string criteria, int expected = 50)
    {
        ContactSearchResult[] results = [];
        await TestExt.When(async () => {
                results = await Tester.FindGroups($"{UniquePart} {criteria}", true, null, expected);
                results.Should().HaveCount(expected, "for criteria '{Criteria}'", criteria);
            },
            TimeSpan.FromSeconds(TestRunnerInfo.IsBuildAgent() ? 60 : 20));
        return results;
    }
}
