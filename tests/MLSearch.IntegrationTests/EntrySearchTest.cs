using ActualChat.Chat;
using ActualChat.Search;
using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;

namespace ActualChat.MLSearch.IntegrationTests;

[Collection(nameof(MLSearchCollection))]
public class EntrySearchTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private BlazorTester Tester { get; } = fixture.AppHost.NewBlazorTester(@out);

    private string UniquePart { get; } = UniqueNames.Prefix();

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldFindNewEntries()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(false);

        var entry1 = await CreateEntry(chatId, "Let's go outside");
        var entry2 = await CreateEntry(chatId, "Saying something loud");
        var entry3 = await CreateEntry(chatId, "Sitting on the river bank");
        var entry4 = await CreateEntry(chatId, "Wake up");

        // act, assert
        var searchResults = await Find("let");
        searchResults.Should().BeEquivalentTo([entry1.BuildSearchResult()], o => o.ExcludingSearchMatch());
        searchResults = await Find("something saying");
        searchResults.Should().BeEquivalentTo([entry2.BuildSearchResult()], o => o.ExcludingSearchMatch());
        searchResults = await Find("river ba");
        searchResults.Should().BeEquivalentTo([entry3.BuildSearchResult()], o => o.ExcludingSearchMatch());
        searchResults = await Find("wak");
        searchResults.Should().BeEquivalentTo([entry4.BuildSearchResult()], o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindOnlyUserRelatedEntries()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, UniquePart);
        var groups = await Tester.CreateGroupContacts(bob, places, UniquePart);
        var people = await Tester.CreateUserContacts(bob, places, UniquePart);
        var entries = await Tester.CreateEntries(bob, groups, people, UniquePart);
        await Tester.SignIn(bob);

        // act
        var expected = entries.Accessible1().BuildSearchResults().ToArray();
        var searchResults = await Find("one", expected: expected.Length);

        // assert
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindNewEntriesOnlyInChat()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob);
        var groups = await Tester.CreateGroupContacts(bob, places);

        // act
        var aliceEntries = await CreateEntries(groups.Joined(), "Let's go outside");
        await CreateEntries(groups.OtherPrivate(), "Let's go - this entry must not be found");
        await Tester.SignIn(bob);
        var bobEntries = await CreateEntries(groups.Joined(), "Let's go");
        var entryLookup = aliceEntries.Concat(bobEntries).ToLookup(x => x.ChatId);

        // assert
        foreach (var chat in groups.Values) {
            var expected = entryLookup[chat.Id]
                .OrderByDescending(x => x.GetIndexedEntryDate())
                .BuildSearchResults()
                .ToList();
            var searchResults = await Find("let", chatId: chat.Id, expected: expected.Count);
            searchResults.Should()
                .BeEquivalentTo(expected, o => o.ExcludingSearchMatch().WithStrictOrdering(), chat.Title);
        }
    }

    [Fact]
    public async Task ShouldFindNewEntriesOnlyInPlace()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob);
        var groups = await Tester.CreateGroupContacts(bob, places);

        // act
        var aliceEntries = await CreateEntries(groups.Joined(), "Let's go outside");
        await CreateEntries(groups.OtherPrivate(), "Let's go - this entry must not be found");
        await Tester.SignIn(bob);
        var bobEntries = await CreateEntries(groups.Joined(), "Let's go");
        var entryLookup = aliceEntries.Concat(bobEntries).ToLookup(x => x.ChatId.PlaceId);

        // assert
        foreach (var place in places.Values) {
            var expected = entryLookup[place.Id]
                .OrderByDescending(x => x.GetIndexedEntryDate())
                .BuildSearchResults()
                .ToList();
            var searchResults = await Find("let", place.Id, expected: expected.Count);
            searchResults.Should()
                .BeEquivalentTo(expected, o => o.ExcludingSearchMatch().WithStrictOrdering(), place.Title);
        }
    }

    [Fact]
    public async Task ShouldFindUpdatedEntries()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(false);

        var entry1 = await CreateEntry(chatId, "Let's go outside");
        var entry2 = await CreateEntry(chatId, "abra cadabra");

        // act
        var searchResults = await Find("let", expected: 1);

        // assert
        searchResults.Should().BeEquivalentTo([entry1.BuildSearchResult()], o => o.ExcludingSearchMatch());

        // act
        entry2 = await UpdateEntry(entry2.Id, "let");
        searchResults = await Find("let", expected: 2);
        searchResults.Should()
            .BeEquivalentTo([entry2.BuildSearchResult(), entry1.BuildSearchResult()],
                o => o.ExcludingSearchMatch().WithStrictOrdering());
    }

    [Fact]
    public async Task ShouldNotFindDeletedEntries()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(false);

        var entry = await CreateEntry(chatId, "Let's go outside");

        // act
        await Find("let", expected: 1);
        await Tester.RemoveTextEntry(entry.Id);
        var searchResults = await Find("let", expected: 0);

        // assert
        searchResults.Should().BeEmpty();
    }

    // Private methods

    private async Task<List<ChatEntry>> CreateEntries(IEnumerable<Chat.Chat> chats, string text)
    {
        var entries = new List<ChatEntry>();
        foreach (var chat in chats)
            entries.Add(await Tester.CreateTextEntry(chat.Id, $"{UniquePart} {text}"));
        return entries;
    }

    private async Task<ChatEntry> CreateEntry(ChatId chatId, string text)
        => await Tester.CreateTextEntry(chatId, $"{UniquePart} {text}");

    private Task<ChatEntry> UpdateEntry(ChatEntryId id, string text)
        => Tester.UpdateTextEntry(id, $"{UniquePart} {text}");

    private async Task<ApiArray<EntrySearchResult>> Find(string criteria, PlaceId? placeId = null, ChatId chatId = default, int expected = 1)
    {
        ApiArray<EntrySearchResult> results = [];
        await TestExt.When(async () => {
                results = await Tester.FindEntries($"{UniquePart} {criteria}", placeId, chatId);
                results.Should().HaveCount(expected);
            },
            TimeSpan.FromSeconds(10));
        return results;
    }
}
