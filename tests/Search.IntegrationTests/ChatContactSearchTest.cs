using ActualChat.Chat;
using ActualChat.Performance;
using ActualChat.Testing.Host;
using ActualChat.Users;
using ActualLab.Generators;

namespace ActualChat.Search.IntegrationTests;

[Collection(nameof(SearchCollection))]
public class ChatContactSearchTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    protected override Task InitializeAsync()
    {
        Tracer.Default = Out.NewTracer();
        return base.InitializeAsync();
    }

    protected override Task DisposeAsync()
    {
        Tracer.Default = Tracer.None;
        return base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldFindAddedChats()
    {
        // arrange
        using var appHost = await NewSearchEnabledAppHost();
        await using var tester = appHost.NewWebClientTester(Out);
        var commander = tester.Commander;
        var searchBackend = appHost.Services.GetRequiredService<ISearchBackend>();
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
        var searchResults = await Find(searchBackend, bob.Id, true, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    BuildSearchResult(bob.Id, publicChat1),
                    BuildSearchResult(bob.Id, publicChat2),
                    BuildSearchResult(bob.Id, publicPlacePublicChat1),
                    BuildSearchResult(bob.Id, publicPlacePublicChat2),
                }
            );
        searchResults = await Find(searchBackend, bob.Id, true, "one");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    BuildSearchResult(bob.Id, publicChat1),
                    BuildSearchResult(bob.Id, publicPlacePublicChat1),
                }
            );

        searchResults = await Find(searchBackend, bob.Id, false, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    BuildSearchResult(bob.Id, privateChat1),
                    BuildSearchResult(bob.Id, privateChat2),
                    BuildSearchResult(bob.Id, publicPlacePrivateChat1),
                    BuildSearchResult(bob.Id, publicPlacePrivateChat2),
                    BuildSearchResult(bob.Id, privatePlacePublicChat1),
                    BuildSearchResult(bob.Id, privatePlacePublicChat2),
                    BuildSearchResult(bob.Id, privatePlacePrivateChat1),
                    BuildSearchResult(bob.Id, privatePlacePrivateChat2),
                }
            );

        searchResults = await Find(searchBackend, bob.Id, false, "two");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    BuildSearchResult(bob.Id, privateChat2),
                    BuildSearchResult(bob.Id, publicPlacePrivateChat2),
                    BuildSearchResult(bob.Id, privatePlacePublicChat2),
                    BuildSearchResult(bob.Id, privatePlacePrivateChat2),
                }
            );
    }

    [Fact]
    public async Task ShouldFindUpdateChats()
    {
        // arrange
        using var appHost = await NewSearchEnabledAppHost();
        await using var tester = appHost.NewWebClientTester(Out);
        var commander = tester.Commander;
        var searchBackend = appHost.Services.GetRequiredService<ISearchBackend>();
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
        var searchResults = await Find(searchBackend, bob.Id, true, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    BuildSearchResult(bob.Id, publicChat1),
                    BuildSearchResult(bob.Id, publicChat2),
                    // BuildSearchResult(bob.Id, publicPlacePublicChat1),
                    BuildSearchResult(bob.Id, publicPlacePublicChat2),
                }
            );

        searchResults = await Find(searchBackend, bob.Id, false, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    BuildSearchResult(bob.Id, privateChat1),
                    BuildSearchResult(bob.Id, privateChat2),
                    BuildSearchResult(bob.Id, publicPlacePrivateChat1),
                    BuildSearchResult(bob.Id, publicPlacePrivateChat2),
                    BuildSearchResult(bob.Id, privatePlacePublicChat1),
                    BuildSearchResult(bob.Id, privatePlacePublicChat2),
                    BuildSearchResult(bob.Id, privatePlacePrivateChat2),
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
        searchResults = await Find(searchBackend, bob.Id, true, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    BuildSearchResult(bob.Id, publicChat1),
                    BuildSearchResult(bob.Id, publicChat2),
                    BuildSearchResult(bob.Id, publicPlacePublicChat1),
                    BuildSearchResult(bob.Id, publicPlacePublicChat2),
                }
            );

        searchResults = await Find(searchBackend, bob.Id, false, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    BuildSearchResult(bob.Id, privateChat1),
                    BuildSearchResult(bob.Id, privateChat2),
                    BuildSearchResult(bob.Id, publicPlacePrivateChat1),
                    BuildSearchResult(bob.Id, publicPlacePrivateChat2),
                    BuildSearchResult(bob.Id, privatePlacePublicChat1),
                    BuildSearchResult(bob.Id, privatePlacePublicChat2),
                    BuildSearchResult(bob.Id, privatePlacePrivateChat1),
                    BuildSearchResult(bob.Id, privatePlacePrivateChat2),
                }
            );

        searchResults = await Find(searchBackend, bob.Id, true, "abra");
        searchResults.Should().BeEmpty();

        searchResults = await Find(searchBackend, bob.Id, false, "abra");
        searchResults.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldNotFindDeletedChats()
    {
        // arrange
        using var appHost = await NewSearchEnabledAppHost();
        await using var tester = appHost.NewWebClientTester(Out);
        var commander = tester.Commander;
        var searchBackend = appHost.Services.GetRequiredService<ISearchBackend>();
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
        var searchResults = await Find(searchBackend, bob.Id, true, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    BuildSearchResult(bob.Id, publicChat1),
                    BuildSearchResult(bob.Id, publicChat2),
                    BuildSearchResult(bob.Id, publicPlacePublicChat1),
                    BuildSearchResult(bob.Id, publicPlacePublicChat2),
                }
            );

        searchResults = await Find(searchBackend, bob.Id, false, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    BuildSearchResult(bob.Id, privateChat1),
                    BuildSearchResult(bob.Id, privateChat2),
                    BuildSearchResult(bob.Id, publicPlacePrivateChat1),
                    BuildSearchResult(bob.Id, publicPlacePrivateChat2),
                    BuildSearchResult(bob.Id, privatePlacePublicChat1),
                    BuildSearchResult(bob.Id, privatePlacePublicChat2),
                    BuildSearchResult(bob.Id, privatePlacePrivateChat1),
                    BuildSearchResult(bob.Id, privatePlacePrivateChat2),
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
        searchResults = await Find(searchBackend, bob.Id, true, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    BuildSearchResult(bob.Id, publicChat1),
                    BuildSearchResult(bob.Id, publicPlacePublicChat2),
                }
            );

        searchResults = await Find(searchBackend, bob.Id, false, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    BuildSearchResult(bob.Id, privateChat2),
                    BuildSearchResult(bob.Id, publicPlacePrivateChat1),
                    BuildSearchResult(bob.Id, privatePlacePublicChat2),
                    BuildSearchResult(bob.Id, privatePlacePrivateChat1),
                }
            );
    }

    private static ApiArray<IndexedChatContact> BuildChatContacts(IEnumerable<Place> places, params Chat.Chat[] chats)
    {
        var placeMap = places.ToDictionary(x => x.Id);
        return chats.Select(x => BuildChatContact(placeMap.GetValueOrDefault(x.Id.PlaceId), x)).ToApiArray();
    }

    private static IndexedChatContact BuildChatContact(Place? place, Chat.Chat chat)
        => BuildChatContact(chat.Id, chat.Title, chat.IsPublic && place is not { IsPublic: false });

    private static IndexedChatContact BuildChatContact(ChatId chatId, string title, bool isPublic)
        => new () {
            Id = chatId,
            Title = title,
            IsPublic = isPublic,
            PlaceId = chatId.PlaceId,
        };

    private static ContactSearchResult BuildSearchResult(UserId ownerId, Chat.Chat chat)
        => BuildSearchResult(ownerId, chat.Id, chat.Title);

    private static ContactSearchResult BuildSearchResult(UserId ownerId, ChatId chatId, string title)
        => new (new ContactId(ownerId, chatId), SearchMatch.New(title));

    private async Task<Place> CreatePlace(WebClientTester tester, bool isPublic, string title)
    {
        var (placeId, _) = await tester.CreatePlace(x => x with {
            IsPublic = isPublic,
            Title = title,
        });
        return await tester.Places.Get(tester.Session, placeId, CancellationToken.None).Require();
    }

    private async Task<Chat.Chat> CreateChat(WebClientTester tester, bool isPublic, string title, PlaceId placeId = default)
    {
        var (id, _) = await tester.CreateChat(x => x with {
            Kind = null,
            Title = title,
            PlaceId = placeId,
            IsPublic = isPublic,
        });
        return await tester.Chats.Get(tester.Session, id, CancellationToken.None).Require();
    }

    private async Task<ApiArray<ContactSearchResult>> Find(ISearchBackend searchBackend, UserId ownerId, bool isPublic, string criteria)
    {
        var searchResults = await searchBackend.FindChatContacts(ownerId,
            isPublic,
            criteria,
            0,
            20,
            CancellationToken.None);
        searchResults.Offset.Should().Be(0);
        return searchResults.Hits;
    }

    private Task<TestAppHost> NewSearchEnabledAppHost()
        => NewAppHost(options => options with {
                AppConfigurationExtender = cfg => {
                    cfg.AddInMemory(("SearchSettings:IsSearchEnabled", "true"));
                },
            });
}
