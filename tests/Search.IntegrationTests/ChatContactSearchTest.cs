using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Search.Module;
using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Search.IntegrationTests;

[Collection(nameof(SearchCollection))]
public class ChatContactSearchTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ShouldFindAddedChats()
    {
        // arrange
        using var appHost = await NewSearchEnabledAppHost(nameof(ShouldFindAddedChats));
        await using var tester = appHost.NewWebClientTester(Out);
        var commander = tester.Commander;
        var bob = await tester.SignInAsBob(RandomStringGenerator.Default.Next());
        var privateChat1 = await CreateChat(tester, false, "Private non-place chat 1 one");
        var privateChat2 = await CreateChat(tester, false, "Private non-place chat 2 two");
        var publicChat1 = await CreateChat(tester, true, "Public non-place chat 1 one");
        var publicChat2 = await CreateChat(tester, true, "Public non-place chat 2 two");
        var privatePlace = await CreatePlace(tester, false, "Bob's private Place");
        var privatePlacePrivateChat1 = await CreateChat(tester, false, "Private place private chat 1 one", privatePlace.Id);
        var privatePlacePrivateChat2 = await CreateChat(tester, false, "Private place private chat 2 two", privatePlace.Id);
        var privatePlacePublicChat1 = await CreateChat(tester, true, "Private place public chat 1 one", privatePlace.Id);
        var privatePlacePublicChat2 = await CreateChat(tester, true, "Private place public chat 2 two", privatePlace.Id);
        var publicPlace = await CreatePlace(tester, true, "Bob's public Place");
        var publicPlacePrivateChat1 = await CreateChat(tester, false, "Public place private chat 1 one", publicPlace.Id);
        var publicPlacePrivateChat2 = await CreateChat(tester, false, "Public place private chat 2 two", publicPlace.Id);
        var publicPlacePublicChat1 = await CreateChat(tester, true, "Public place public chat 1 one", publicPlace.Id);
        var publicPlacePublicChat2 = await CreateChat(tester, true, "Public place public chat 2 two", publicPlace.Id);

        // act
        var updates = BuildChatContacts(
            new[] { privatePlace, publicPlace },
            privateChat1,
            privateChat2,
            publicChat1,
            publicChat2,
            privatePlacePrivateChat1,
            privatePlacePrivateChat2,
            privatePlacePublicChat1,
            privatePlacePublicChat2,
            publicPlacePrivateChat1,
            publicPlacePrivateChat2,
            publicPlacePublicChat1,
            publicPlacePublicChat2);
        await commander.Call(new SearchBackend_ChatContactBulkIndex(updates, ApiArray<IndexedChatContact>.Empty));
        await commander.Call(new SearchBackend_Refresh(refreshPrivateChats: true, refreshPublicChats: true));

        // assert
        var searchResults = await Find(tester, true, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    publicChat1.ToSearchResult(bob.Id),
                    publicChat2.ToSearchResult(bob.Id),
                    publicPlacePublicChat1.ToSearchResult(bob.Id),
                    publicPlacePublicChat2.ToSearchResult(bob.Id),
                }
            );
        searchResults = await Find(tester, true, "one");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    publicChat1.ToSearchResult(bob.Id),
                    publicPlacePublicChat1.ToSearchResult(bob.Id),
                }
            );

        searchResults = await Find(tester, false, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    privateChat1.ToSearchResult(bob.Id),
                    privateChat2.ToSearchResult(bob.Id),
                    publicPlacePrivateChat1.ToSearchResult(bob.Id),
                    publicPlacePrivateChat2.ToSearchResult(bob.Id),
                    privatePlacePublicChat1.ToSearchResult(bob.Id),
                    privatePlacePublicChat2.ToSearchResult(bob.Id),
                    privatePlacePrivateChat1.ToSearchResult(bob.Id),
                    privatePlacePrivateChat2.ToSearchResult(bob.Id),
                }
            );

        searchResults = await Find(tester, false, "two");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    privateChat2.ToSearchResult(bob.Id),
                    publicPlacePrivateChat2.ToSearchResult(bob.Id),
                    privatePlacePublicChat2.ToSearchResult(bob.Id),
                    privatePlacePrivateChat2.ToSearchResult(bob.Id),
                }
            );
    }

    [Fact]
    public async Task ShouldFindUpdateChats()
    {
        // arrange
        using var appHost = await NewSearchEnabledAppHost(nameof(ShouldFindUpdateChats));
        await using var tester = appHost.NewWebClientTester(Out);
        var commander = tester.Commander;
        var bob = await tester.SignInAsBob(RandomStringGenerator.Default.Next());
        var privateChat1 = await CreateChat(tester, false, "Private non-place chat 1");
        var privateChat2 = await CreateChat(tester, false, "Private non-place chat 2");
        var publicChat1 = await CreateChat(tester, true, "Public non-place chat 1");
        var publicChat2 = await CreateChat(tester, true, "Public non-place chat 2");
        var privatePlace = await CreatePlace(tester, false, "Bob's private Place");
        var privatePlacePrivateChat1 = await CreateChat(tester, false, "Private place private chat 1", privatePlace.Id);
        var privatePlacePrivateChat2 = await CreateChat(tester, false, "Private place private chat 2", privatePlace.Id);
        var privatePlacePublicChat1 = await CreateChat(tester, true, "Private place public chat 1", privatePlace.Id);
        var privatePlacePublicChat2 = await CreateChat(tester, true, "Private place public chat 2", privatePlace.Id);
        var publicPlace = await CreatePlace(tester, true, "Bob's public Place");
        var publicPlacePrivateChat1 = await CreateChat(tester, false, "Public place private chat 1", publicPlace.Id);
        var publicPlacePrivateChat2 = await CreateChat(tester, false, "Public place private chat 2", publicPlace.Id);
        var publicPlacePublicChat1 = await CreateChat(tester, true, "Public place public chat 1", publicPlace.Id);
        var publicPlacePublicChat2 = await CreateChat(tester, true, "Public place public chat 2", publicPlace.Id);

        // act
        var updates = BuildChatContacts(
            new[] { privatePlace, publicPlace },
            privateChat1,
            privateChat2,
            publicChat1,
            publicChat2,
            privatePlacePrivateChat1 with { Title = "abra cadabra" },
            privatePlacePrivateChat2,
            privatePlacePublicChat1,
            privatePlacePublicChat2,
            publicPlacePrivateChat1,
            publicPlacePrivateChat2,
            publicPlacePublicChat1 with { Title = "abra cadabra" },
            publicPlacePublicChat2);
        await commander.Call(new SearchBackend_ChatContactBulkIndex(updates, ApiArray<IndexedChatContact>.Empty));
        await commander.Call(new SearchBackend_Refresh(refreshPrivateChats: true, refreshPublicChats: true));

        // assert
        var searchResults = await Find(tester, true, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    publicChat1.ToSearchResult(bob.Id),
                    publicChat2.ToSearchResult(bob.Id),
                    publicPlacePublicChat2.ToSearchResult(bob.Id),
                }
            );

        searchResults = await Find(tester, false, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    privateChat1.ToSearchResult(bob.Id),
                    privateChat2.ToSearchResult(bob.Id),
                    publicPlacePrivateChat1.ToSearchResult(bob.Id),
                    publicPlacePrivateChat2.ToSearchResult(bob.Id),
                    privatePlacePublicChat1.ToSearchResult(bob.Id),
                    privatePlacePublicChat2.ToSearchResult(bob.Id),
                    privatePlacePrivateChat2.ToSearchResult(bob.Id),
                }
            );

        // act
        updates = BuildChatContacts(
            new[] { privatePlace, publicPlace },
            privatePlacePrivateChat1,
            publicPlacePublicChat1);
        await commander.Call(new SearchBackend_ChatContactBulkIndex(updates, ApiArray<IndexedChatContact>.Empty));
        await commander.Call(new SearchBackend_Refresh(refreshPrivateChats: true, refreshPublicChats: true));

        // assert
        searchResults = await Find(tester, true, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    publicChat1.ToSearchResult(bob.Id),
                    publicChat2.ToSearchResult(bob.Id),
                    publicPlacePublicChat1.ToSearchResult(bob.Id),
                    publicPlacePublicChat2.ToSearchResult(bob.Id),
                }
            );

        searchResults = await Find(tester, false, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    privateChat1.ToSearchResult(bob.Id),
                    privateChat2.ToSearchResult(bob.Id),
                    publicPlacePrivateChat1.ToSearchResult(bob.Id),
                    publicPlacePrivateChat2.ToSearchResult(bob.Id),
                    privatePlacePublicChat1.ToSearchResult(bob.Id),
                    privatePlacePublicChat2.ToSearchResult(bob.Id),
                    privatePlacePrivateChat1.ToSearchResult(bob.Id),
                    privatePlacePrivateChat2.ToSearchResult(bob.Id),
                }
            );

        searchResults = await Find(tester, true, "abra");
        searchResults.Should().BeEmpty();

        searchResults = await Find(tester, false, "abra");
        searchResults.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldNotFindDeletedChats()
    {
        // arrange
        using var appHost = await NewSearchEnabledAppHost(nameof(ShouldFindUpdateChats));
        await using var tester = appHost.NewWebClientTester(Out);
        var commander = tester.Commander;
        var bob = await tester.SignInAsBob();
        var privateChat1 = await CreateChat(tester, false, "Private non-place chat 1 one");
        var privateChat2 = await CreateChat(tester, false, "Private non-place chat 2 two");
        var publicChat1 = await CreateChat(tester, true, "Public non-place chat 1 one");
        var publicChat2 = await CreateChat(tester, true, "Public non-place chat 2 two");
        var privatePlace = await CreatePlace(tester, false, "Bob's private Place");
        var privatePlacePrivateChat1 = await CreateChat(tester, false, "Private place private chat 1 one", privatePlace.Id);
        var privatePlacePrivateChat2 = await CreateChat(tester, false, "Private place private chat 2 two", privatePlace.Id);
        var privatePlacePublicChat1 = await CreateChat(tester, true, "Private place public chat 1 one", privatePlace.Id);
        var privatePlacePublicChat2 = await CreateChat(tester, true, "Private place public chat 2 two", privatePlace.Id);
        var publicPlace = await CreatePlace(tester, true, "Bob's public Place");
        var publicPlacePrivateChat1 = await CreateChat(tester, false, "Public place private chat 1 one", publicPlace.Id);
        var publicPlacePrivateChat2 = await CreateChat(tester, false, "Public place private chat 2 two", publicPlace.Id);
        var publicPlacePublicChat1 = await CreateChat(tester, true, "Public place public chat 1 one", publicPlace.Id);
        var publicPlacePublicChat2 = await CreateChat(tester, true, "Public place public chat 2 two", publicPlace.Id);

        // act
        var updates = BuildChatContacts(
            new[] { privatePlace, publicPlace },
            privateChat1,
            privateChat2,
            publicChat1,
            publicChat2,
            privatePlacePrivateChat1,
            privatePlacePrivateChat2,
            privatePlacePublicChat1,
            privatePlacePublicChat2,
            publicPlacePrivateChat1,
            publicPlacePrivateChat2,
            publicPlacePublicChat1,
            publicPlacePublicChat2);
        await commander.Call(new SearchBackend_ChatContactBulkIndex(updates, ApiArray<IndexedChatContact>.Empty));
        await commander.Call(new SearchBackend_Refresh(refreshPrivateChats: true, refreshPublicChats: true));

        // assert
        var searchResults = await Find(tester, true, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    publicChat1.ToSearchResult(bob.Id),
                    publicChat2.ToSearchResult(bob.Id),
                    publicPlacePublicChat1.ToSearchResult(bob.Id),
                    publicPlacePublicChat2.ToSearchResult(bob.Id),
                }
            );

        searchResults = await Find(tester, false, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    privateChat1.ToSearchResult(bob.Id),
                    privateChat2.ToSearchResult(bob.Id),
                    publicPlacePrivateChat1.ToSearchResult(bob.Id),
                    publicPlacePrivateChat2.ToSearchResult(bob.Id),
                    privatePlacePublicChat1.ToSearchResult(bob.Id),
                    privatePlacePublicChat2.ToSearchResult(bob.Id),
                    privatePlacePrivateChat1.ToSearchResult(bob.Id),
                    privatePlacePrivateChat2.ToSearchResult(bob.Id),
                }
            );

        // act
        await commander.Call(new SearchBackend_ChatContactBulkIndex(ApiArray<IndexedChatContact>.Empty,
            BuildChatContacts(new[] { privatePlace, publicPlace, },
                publicChat2,
                publicPlacePublicChat1,
                privateChat1,
                publicPlacePrivateChat2,
                privatePlacePublicChat1,
                privatePlacePrivateChat2)));
        await commander.Call(new SearchBackend_Refresh(refreshPrivateChats: true, refreshPublicChats: true));

        // assert
        searchResults = await Find(tester, true, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    publicChat1.ToSearchResult(bob.Id),
                    publicPlacePublicChat2.ToSearchResult(bob.Id),
                }
            );

        searchResults = await Find(tester, false, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    privateChat2.ToSearchResult(bob.Id),
                    publicPlacePrivateChat1.ToSearchResult(bob.Id),
                    privatePlacePublicChat2.ToSearchResult(bob.Id),
                    privatePlacePrivateChat1.ToSearchResult(bob.Id),
                }
            );
    }

    // Private methods

    private static ApiArray<IndexedChatContact> BuildChatContacts(IEnumerable<Place> places, params Chat.Chat[] chats)
    {
        var placeMap = places.ToDictionary(x => x.Id);
        return chats.Select(x => x.ToIndexedChatContact(placeMap.GetValueOrDefault(x.Id.PlaceId))).ToApiArray();
    }

    private async Task<Place> CreatePlace(WebClientTester tester, bool isPublic, string title)
    {
        var (placeId, _) = await tester.CreatePlace(isPublic, title);
        return await tester.Places.Get(tester.Session, placeId, CancellationToken.None).Require();
    }

    private async Task<Chat.Chat> CreateChat(WebClientTester tester, bool isPublic, string title, PlaceId placeId = default)
    {
        var (id, _) = await tester.CreateChat(isPublic, title, placeId);
        return await tester.Chats.Get(tester.Session, id, CancellationToken.None).Require();
    }

    private async Task<ApiArray<ContactSearchResult>> Find(IWebTester tester, bool isPublic, string criteria)
    {
        var account = await tester.GetOwnAccount();
        var searchResults = await tester.SearchBackend.FindContacts(account.Id,
            new () {
                Criteria = criteria,
                IsPublic = isPublic,
                Kind = ContactKind.Chat,
                Limit = 20,
            },
            CancellationToken.None);
        searchResults.Offset.Should().Be(0);
        return searchResults.Hits;
    }

    private Task<TestAppHost> NewSearchEnabledAppHost(string instanceName)
        => NewAppHost(instanceName,
            options => options with {
                ConfigureHost = (_, cfg) => {
                    cfg.AddInMemoryCollection(($"{nameof(SearchSettings)}:{nameof(SearchSettings.IsSearchEnabled)}", "true"));
                },
            });
}
