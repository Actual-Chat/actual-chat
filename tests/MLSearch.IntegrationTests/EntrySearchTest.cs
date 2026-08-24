using ActualChat.Search;
using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;

namespace ActualChat.MLSearch.IntegrationTests;

[Collection(nameof(MLSearchCollection))]
public class EntrySearchTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);

    private string IsolationKey { get; } = UniqueNames.Random();

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
        searchResults.Should()
            .BeEquivalentTo([entry1.BuildSearchResult(["let's"], IsolationKey)],
                o => o.ExcludingSearchMatch());
        searchResults = await Find("something saying");
        searchResults.Should()
            .BeEquivalentTo([entry2.BuildSearchResult(["something", "saying"], IsolationKey)],
                o => o.ExcludingSearchMatch());
        searchResults = await Find("river ba");
        searchResults.Should()
            .BeEquivalentTo([entry3.BuildSearchResult(["river", "bank"], IsolationKey)],
                o => o.ExcludingSearchMatch());
        searchResults = await Find("wak");
        searchResults.Should()
            .BeEquivalentTo([entry4.BuildSearchResult(["wake"], IsolationKey)], o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindEntriesByHashtag()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(false);

        var tagged = await CreateEntry(chatId, "Check the #Promo today");
        var otherTagged = await CreateEntry(chatId, "Big #promotion starts");
        var untagged = await CreateEntry(chatId, "No promo here");

        // act, assert
        // A trailing space completes the tag, so it must match exactly - and case-insensitively
        var searchResults = await Find("#PROMO ");
        searchResults.Should()
            .BeEquivalentTo([tagged.BuildSearchResult(["promo"], IsolationKey)], o => o.ExcludingSearchMatch());

        // A tag ending the criteria may still be half-typed, so it prefix-matches
        searchResults = await Find("#promo", expected: 2);
        searchResults.Should()
            .BeEquivalentTo([
                    tagged.BuildSearchResult(["promo"], IsolationKey),
                    otherTagged.BuildSearchResult(["promotion"], IsolationKey),
                ],
                o => o.ExcludingSearchMatch());

        // Without '#' it's an ordinary word search, which finds tagged and untagged alike
        searchResults = await Find("promo", expected: 3);
        searchResults.Should()
            .BeEquivalentTo([
                    tagged.BuildSearchResult(["promo"], IsolationKey),
                    otherTagged.BuildSearchResult(["promotion"], IsolationKey),
                    untagged.BuildSearchResult(["promo"], IsolationKey),
                ],
                o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldShowCorrectHighlight()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(false);

        var entry = await CreateEntry(chatId,
            "Lorem Ipsum is simply dummy text of the printing and typesetting industry. "
            + "Lorem Ipsum test has been the industry's standard dummy text ever since the 1500s, "
            + "when an unknown printer took a galley of type and scrambled it to make a type specimen book.");

        // act, assert
        var searchResults = await Find("test");
        searchResults.Should()
            .BeEquivalentTo([
                entry.Id.BuildSearchResult(
                    "…Lorem Ipsum test has been the industry's standard dummy text ever since the 1500s, "
                    + "when an unknown printer…",
                    ["test"],
                    IsolationKey,
                    (13, 17)),
            ]);
    }

    [Fact]
    public async Task ShouldFindLinksByPart()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(false);

        var entry = await CreateEntry(chatId, $"https://{Constants.Hosts.Voxt}");

        // act, assert
        var searchResults = await Find("voxt");
        // var searchResults = await Find("chat"); TODO: uncomment when links are handled properly
        searchResults.Should()
            .BeEquivalentTo([
                entry.BuildSearchResult(
                    [Constants.Hosts.Voxt],
                    IsolationKey,
                    (8, 15)),
            ]);
    }

    [Fact]
    public async Task ShouldFindOnlyUserRelatedEntries()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, IsolationKey);
        var groups = await Tester.CreateGroupContacts(bob, places, IsolationKey);
        var people = await Tester.CreateUserContacts(bob, places, IsolationKey);
        var entries = await Tester.CreateEntries(bob, groups, people, IsolationKey);
        await Tester.SignIn(bob);

        // act
        var expected = entries.Accessible1().BuildSearchResults([TestSearchDataGenerator.OneTerm], IsolationKey);
        var searchResults = await Find(TestSearchDataGenerator.OneTerm, expected: expected.Count);

        // assert
        searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch());
    }

    [Fact]
    public async Task ShouldFindNewEntriesOnlyInChat()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, IsolationKey);
        var groups = await Tester.CreateGroupContacts(bob, places, IsolationKey);

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
                .BuildSearchResults(["let's"], IsolationKey);
            var searchResults = await Find("let", chatId: chat.Id, expected: expected.Count);
            searchResults.Should()
                .BeEquivalentTo(expected, o => o.ExcludingSearchMatch().WithStrictOrderingFor(x => x), chat.Title);
        }
    }

    [Fact]
    public async Task ShouldFindNewEntriesOnlyInPlace()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob, IsolationKey);
        var groups = await Tester.CreateGroupContacts(bob, places, IsolationKey);
        var people = await Tester.CreateUserContacts(bob, places, IsolationKey);

        // act
        var allPlaceEntries = new List<(PlaceId? PlaceId, ChatEntry Entry)>();
        var aliceEntries = await CreateEntries(groups.Joined(), "Let's go outside");
        allPlaceEntries.AddRange(aliceEntries.Select(x => ((x.ChatId as PlaceChatId)?.PlaceId, x)));
        await CreateEntries(groups.OtherPrivate(), "Let's go - this entry must not be found");
        await Tester.SignIn(bob);
        var bobEntries = await CreateEntries(groups.Joined(), "Let's go");
        allPlaceEntries.AddRange(bobEntries.Select(x => ((x.ChatId as PlaceChatId)?.PlaceId, x)));
        foreach (var userContact in people) {
            var entry = await CreateEntry(PeerChatId.New(bob.Id, userContact.Value.Id), "Let's go - to a peer chat");
            if (userContact.Key.PlaceKey is { } placeKey) {
                var place = places[placeKey];
                allPlaceEntries.Add((place.Id, entry));
            }
        }
        var entryLookup = allPlaceEntries.ToLookup(x => x.PlaceId, x => x.Entry);

        // assert
        foreach (var place in places.Values) {
            var expected = entryLookup[place.Id]
                .OrderByDescending(x => x.GetIndexedEntryDate())
                .BuildSearchResults(["let's"], IsolationKey);
            var searchResults = await Find("let", place.Id, expected: expected.Count);
            searchResults.Should()
                .BeEquivalentTo(expected, o => o.ExcludingSearchMatch().WithStrictOrderingFor(x => x), place.Title);
        }
    }

    [Fact]
    public async Task ShouldNotFindEntriesWhenPlaceHasNoAccessibleChats()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(false);
        await CreateEntry(chatId, "Let's go outside");
        await Find("let", expected: 1);

        // act
        await Tester.SignInAsUniqueAlice();
        var place = await Tester.CreatePlace(false, $"empty place {IsolationKey}");
        var searchResults = await Tester.FindEntries($"{IsolationKey} let", place.Id);

        // assert
        searchResults.Should().BeEmpty();
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
        searchResults.Should()
            .BeEquivalentTo([entry1.BuildSearchResult(["let's"], IsolationKey)],
                o => o.ExcludingSearchMatch());

        // act
        entry2 = await UpdateEntry(entry2.Id, "let");
        searchResults = await Find("let", expected: 2);
        searchResults.Should()
            .BeEquivalentTo([
                    entry2.BuildSearchResult(["let"], IsolationKey),
                    entry1.BuildSearchResult(["let's"], IsolationKey),
                ],
                o => o.ExcludingSearchMatch().WithStrictOrderingFor(x => x));
    }

    [Fact]
    public async Task ShouldNotFindDeletedEntries()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(false);

        var entry = await CreateEntry(chatId, "Let's go outside");

        // act
        await Find("let's", expected: 1);
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
            entries.Add(await CreateEntry(chat.Id, text));
        return entries;
    }

    private async Task<ChatEntry> CreateEntry(ChatId chatId, string text)
        => await Tester.CreateTextEntry(chatId, $"{text} {IsolationKey}");

    private Task<ChatEntry> UpdateEntry(ChatEntryId id, string text)
        => Tester.UpdateTextEntry(id, $"{text} {IsolationKey}");

    private Task<FoundChatEntry[]> Find(
        string criteria,
        PlaceId? placeId = null,
        ChatId chatId = null!,
        int expected = 1)
        => TestsExt.When(async () => {
                var results = await Tester.FindEntries($"{IsolationKey} {criteria}", placeId, chatId);
                results.Should().HaveCount(expected);
                return results;
            },
            TestRunnerInfo.IsBuildAgent() ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(20));
}
