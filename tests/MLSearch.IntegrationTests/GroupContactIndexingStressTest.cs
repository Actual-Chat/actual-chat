using System.Diagnostics.CodeAnalysis;
using ActualChat.Search;
using ActualChat.Testing.Host;

namespace ActualChat.MLSearch.IntegrationTests;

[Collection(nameof(SlowMLSearchCollection))]
[Trait("Category", "Slow")]
public class GroupContactIndexingStressTest(SlowAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<SlowAppHostFixture>(fixture, @out)
{
    [field: AllowNull, MaybeNull]
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);

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
        => Enumerable.Range(1, count).Select(i => CreateGroup($"{prefix} {i}")).Collect(Environment.ProcessorCount / 2);

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
                results.Should().HaveCount(expected, "for criteria '{0}'", criteria);
            },
            TestRunnerInfo.IsBuildAgent() ? TimeSpan.FromSeconds(90) : TimeSpan.FromSeconds(30));
        return results;
    }
}
