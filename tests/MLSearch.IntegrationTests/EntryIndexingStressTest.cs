using ActualChat.Chat;
using ActualChat.Search;
using ActualChat.Testing.Host;

namespace ActualChat.MLSearch.IntegrationTests;

[Collection(nameof(MLSearchCollection))]
[Trait("Category", "Slow")]
public class EntryIndexingStressTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private BlazorTester Tester { get; } = fixture.AppHost.NewBlazorTester(@out);

    private string UniquePart { get; } = UniqueNames.Prefix();

    protected override Task InitializeAsync()
    {
        ThreadPool.SetMinThreads(1024, 1024);
        ThreadPool.SetMaxThreads(1024, 1024);
        return base.InitializeAsync();
    }

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1_000)]
    [InlineData(3_456)]
    public async Task ShouldIndexManyEntries(int portionSize)
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(false);
        var portion1 = await CreateEntries(chatId, portionSize, "The first portion:");
        var portion2 = await CreateEntries(chatId, 50, "The second portion:");

        // act
        var searchResults = await Find("first", expected: 50);

        // assert
        searchResults.Select(x => x.Text)
            .Should()
            .OnlyHaveUniqueItems()
            .And.BeSubsetOf(portion1.Select(x => x.Content));

        // act
        searchResults = await Find("second", expected: 50);

        // assert
        searchResults.Select(x => x.Text)
            .Should()
            .OnlyHaveUniqueItems()
            .And.BeSubsetOf(portion2.Select(x => x.Content));
    }

    // Private methods

    private Task<ChatEntry[]> CreateEntries(ChatId chatId, int count, string prefix)
        => Enumerable.Range(1, count).Select(i => CreateEntry(chatId, $"{prefix} {i}")).Collect(100);

    private Task<ChatEntry> CreateEntry(ChatId chatId, string text)
        => Tester.CreateTextEntry(chatId, $"{text} {UniquePart}");

    private Task<EntrySearchResult[]> Find(
        string criteria,
        PlaceId? placeId = null,
        ChatId? chatId = null,
        int expected = 1)
        => TestsExt.When(async () => {
                var results = await Tester.FindEntries($"{UniquePart} {criteria}", placeId, chatId);
                results.Should().HaveCount(expected, "for chat #{0} and criteria '{1}'", chatId, criteria);
                return results;
            },
            TimeSpan.FromSeconds(60));
}
