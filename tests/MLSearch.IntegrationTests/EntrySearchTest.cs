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
    public async Task ShouldFindNewEntriesOnlyInChat()
    {
        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignInAsUniqueAlice();
        var places = await Tester.CreatePlaceContacts(bob);
        var groups = await Tester.CreateGroupContacts(bob, places);

        // act
        var aliceEntries = await groups.Joined().Select(x => CreateEntry(x.Id, "Let's go outside")).Collect();
        await groups.OtherPrivate().Select(x => CreateEntry(x.Id, "Let's go - this entry must not be found")).Collect();
        await Tester.SignIn(bob);
        var bobEntries = await groups.Joined().Select(x => CreateEntry(x.Id, "Let's go")).Collect();
        var entryLookup = aliceEntries.Concat(bobEntries).ToLookup(x => x.ChatId);

        // assert
        foreach (var chat in groups.Values) {
            var expected = entryLookup[chat.Id].BuildSearchResults().ToList();
            var searchResults = await Find("let", chatId: chat.Id, expected: expected.Count);
            searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch(), chat.Title);
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
        var aliceEntries = await groups.Joined().Select(x => CreateEntry(x.Id, "Let's go outside")).Collect();
        await groups.OtherPrivate().Select(x => CreateEntry(x.Id, "Let's go - this entry must not be found")).Collect();
        await Tester.SignIn(bob);
        var bobEntries = await groups.Joined().Select(x => CreateEntry(x.Id, "Let's go")).Collect();
        var entryLookup = aliceEntries.Concat(bobEntries).ToLookup(x => x.ChatId.PlaceId);

        // assert
        foreach (var place in places.Values) {
            var expected = entryLookup[place.Id].BuildSearchResults().ToList();
            var searchResults = await Find("let", place.Id, expected: expected.Count);
            searchResults.Should().BeEquivalentTo(expected, o => o.ExcludingSearchMatch(), place.Title);
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
            .BeEquivalentTo([entry1.BuildSearchResult(), entry2.BuildSearchResult()], o => o.ExcludingSearchMatch());
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

    // [Fact]
    // public async Task ShouldAddForPlace()
    // {
    //     // arrange
    //     var bob = await Tester.SignInAsBob();
    //     var place = await Tester.CreatePlace(false, "Bob's Place");
    //     var publicChat1Id = await CreateChat(ChatKind.Group, "Public Group Chat 1", place.Id);
    //     var publicChat2Id = await CreateChat(ChatKind.Group, "Public Group Chat 1", place.Id);
    //     var privateChat1Id = await CreateChat(ChatKind.Group, "Private Group Chat 1", place.Id);
    //     var privateChat2Id = await CreateChat(ChatKind.Group, "Private Group Chat 1", place.Id);
    //
    //     var publicChat1Updates = ApiArray.New(
    //         BuildEntry(publicChat1Id, 1, "PublicChat1: Let's go outside"),
    //         BuildEntry(publicChat1Id, 2, "PublicChat1: Saying something loud"),
    //         BuildEntry(publicChat1Id, 3, "PublicChat1: Sitting on the river bank"),
    //         BuildEntry(publicChat1Id, 4, "PublicChat1: Wake up"));
    //
    //     var publicChat2Updates = ApiArray.New(
    //         BuildEntry(publicChat2Id, 1, "PublicChat2: Let's go outside"),
    //         BuildEntry(publicChat2Id, 2, "PublicChat2: Saying something loud"),
    //         BuildEntry(publicChat2Id, 3, "PublicChat2: Sitting on the river bank"),
    //         BuildEntry(publicChat2Id, 4, "PublicChat2: Wake up"));
    //
    //     var privateChat1Updates = ApiArray.New(
    //         BuildEntry(privateChat1Id, 1, "PrivateChat1: Let's go outside"),
    //         BuildEntry(privateChat1Id, 2, "PrivateChat1: Saying something loud"),
    //         BuildEntry(privateChat1Id, 3, "PrivateChat1: Sitting on the river bank"),
    //         BuildEntry(privateChat1Id, 4, "PrivateChat1: Wake up"));
    //
    //     var privateChat2Updates = ApiArray.New(
    //         BuildEntry(privateChat2Id, 1, "PrivateChat2: Let's go outside"),
    //         BuildEntry(privateChat2Id, 2, "PrivateChat2: Saying something loud"),
    //         BuildEntry(privateChat2Id, 3, "PrivateChat2: Sitting on the river bank"),
    //         BuildEntry(privateChat2Id, 4, "PrivateChat2: Wake up"));
    //
    //     // act
    //     await Commander.Call(new SearchBackend_EntryBulkIndex(publicChat1Id, publicChat1Updates,  ApiArray<IndexedEntry>.Empty));
    //     await Commander.Call(new SearchBackend_EntryBulkIndex(publicChat2Id, publicChat2Updates,  ApiArray<IndexedEntry>.Empty));
    //     await Commander.Call(new SearchBackend_EntryBulkIndex(privateChat1Id, privateChat1Updates,  ApiArray<IndexedEntry>.Empty));
    //     await Commander.Call(new SearchBackend_EntryBulkIndex(privateChat2Id, privateChat2Updates,  ApiArray<IndexedEntry>.Empty));
    //     await Commander.Call(new SearchBackend_Refresh(publicChat1Id, publicChat2Id, privateChat1Id, privateChat2Id));
    //
    //     // assert
    //     var publicChat1SearchResults = await _sut.FindEntriesInChat(publicChat1Id,
    //         "wak",
    //         0,
    //         20,
    //         CancellationToken.None);
    //     publicChat1SearchResults.Should()
    //         .BeEquivalentTo(BuildResponse(BuildSearchResult(publicChat1Id, 4, "PublicChat1: Wake up")));
    //     var publicChat2SearchResults = await _sut.FindEntriesInChat(publicChat2Id,
    //         "wak",
    //         0,
    //         20,
    //         CancellationToken.None);
    //     publicChat2SearchResults.Should()
    //         .BeEquivalentTo(BuildResponse(BuildSearchResult(publicChat2Id, 4, "PublicChat2: Wake up")));
    //     var privateChat1SearchResults = await _sut.FindEntriesInChat(privateChat1Id,
    //         "wak",
    //         0,
    //         20,
    //         CancellationToken.None);
    //     privateChat1SearchResults.Should()
    //         .BeEquivalentTo(BuildResponse(BuildSearchResult(privateChat1Id, 4, "PrivateChat1: Wake up")));
    //     var privateChat2SearchResults = await _sut.FindEntriesInChat(privateChat2Id,
    //         "wak",
    //         0,
    //         20,
    //         CancellationToken.None);
    //     privateChat2SearchResults.Should()
    //         .BeEquivalentTo(BuildResponse(BuildSearchResult(privateChat2Id, 4, "PrivateChat2: Wake up")));
    //     var globalSearchResults = await _sut.FindEntriesInAllChats(bob.Id,
    //         "wak",
    //         0,
    //         20,
    //         CancellationToken.None);
    //     globalSearchResults.Should()
    //         .BeEquivalentTo(BuildResponse(
    //                 BuildSearchResult(publicChat1Id, 4, "PublicChat1: Wake up"),
    //                 BuildSearchResult(publicChat2Id, 4, "PublicChat2: Wake up"),
    //                 BuildSearchResult(privateChat1Id, 4, "PrivateChat1: Wake up"),
    //                 BuildSearchResult(privateChat2Id, 4, "PrivateChat2: Wake up")));
    // }
    //
    // [Fact]
    // public async Task ShouldFindIfUpdatedMatchesCriteria()
    // {
    //     // arrange
    //     await Tester.SignInAsBob();
    //     var chatId = await CreateChat();
    //     var updates = ApiArray.New(
    //         BuildEntry(chatId, 1, "Let's go outside"),
    //         BuildEntry(chatId, 2, "Saying something loud"),
    //         BuildEntry(chatId, 3, "Sitting on the river bank"),
    //         BuildEntry(chatId, 4, "Wake up"));
    //     await Commander.Call(new SearchBackend_EntryBulkIndex(chatId, updates,  ApiArray<IndexedEntry>.Empty));
    //     await Commander.Call(new SearchBackend_Refresh(chatId));
    //
    //     // act
    //     updates = ApiArray.New(BuildEntry(chatId, 3, "Waking up..."));
    //     await Commander.Call(new SearchBackend_EntryBulkIndex(chatId, updates,  ApiArray<IndexedEntry>.Empty));
    //     await Commander.Call(new SearchBackend_Refresh(chatId));
    //
    //     // assert
    //     var searchResults = await _sut.FindEntriesInChat(chatId,
    //         "wak",
    //         0,
    //         20,
    //         CancellationToken.None);
    //     searchResults.Should()
    //         .BeEquivalentTo(BuildResponse(BuildSearchResult(chatId, 3, "Waking up..."),
    //             BuildSearchResult(chatId, 4, "Wake up")));
    // }
    //
    // [Fact]
    // public async Task ShouldNotFindDeleted()
    // {
    //     // arrange
    //     await Tester.SignInAsBob();
    //     var chatId = await CreateChat();
    //     var updates = ApiArray.New(
    //         BuildEntry(chatId, 1, "Let's go outside"),
    //         BuildEntry(chatId, 2, "Saying something loud"),
    //         BuildEntry(chatId, 3, "Sitting on the river bank"),
    //         BuildEntry(chatId, 4, "Wake up"));
    //     await Commander.Call(new SearchBackend_EntryBulkIndex(chatId, updates,  ApiArray<IndexedEntry>.Empty));
    //     await Commander.Call(new SearchBackend_Refresh(chatId));
    //
    //     // act
    //     updates = ApiArray.New(BuildEntry(chatId, 3, "Waking up..."));
    //     var removes = ApiArray.New(BuildEntry(chatId, 4L, ""));
    //     await Commander.Call(new SearchBackend_EntryBulkIndex(chatId, updates,  removes));
    //     await Commander.Call(new SearchBackend_Refresh(chatId));
    //
    //     // assert
    //     var searchResults = await _sut.FindEntriesInChat(chatId,
    //         "wak",
    //         0,
    //         20,
    //         CancellationToken.None);
    //     searchResults.Should()
    //         .BeEquivalentTo(BuildResponse(BuildSearchResult(chatId, 3, "Waking up...")));
    // }

    // Private methods

    private Task<ChatEntry> CreateEntry(ChatId chatId, string text)
        => Tester.CreateTextEntry(chatId, $"{UniquePart} {text}");

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
