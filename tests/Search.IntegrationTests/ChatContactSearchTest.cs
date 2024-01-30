using ActualChat.App.Server;
using ActualChat.Chat;
using ActualChat.Performance;
using ActualChat.Testing.Host;

namespace ActualChat.Search.IntegrationTests;

public class ChatContactSearchTest(ITestOutputHelper @out) : AppHostTestBase(@out)
{
    private WebClientTester _tester = null!;
    private ISearchBackend _sut = null!;
    private AppHost _appHost = null!;
    private ICommander _commander = null!;

    public override async Task InitializeAsync()
    {
        Tracer.Default = Out.NewTracer();
        _appHost = await NewAppHost();
        _tester = _appHost.NewWebClientTester();
        _sut = _appHost.Services.GetRequiredService<ISearchBackend>();
        _commander = _appHost.Services.Commander();
    }

    public override async Task DisposeAsync()
    {
        Tracer.Default = Tracer.None;
        await _tester.DisposeAsync().AsTask();
        _appHost.Dispose();
    }

    [Fact]
    public async Task ShouldFindAddedChats()
    {
        // arrange
        var bob = await _tester.SignInAsBob();
        var privateChat1 = await CreateChat(false, "Private non-place chat 1");
        var privateChat2 = await CreateChat(false, "Private non-place chat 2");
        var publicChat1 = await CreateChat(true, "Public non-place chat 1");
        var publicChat2 = await CreateChat(true, "Public non-place chat 2");
        var privatePlace = await CreatePlace(false, "Bob's private Place");
        var privatePlacePrivateChat1 = await CreateChat(false, "Private place private chat 1", privatePlace.Id);
        var privatePlacePrivateChat2 = await CreateChat(false, "Private place private chat 2", privatePlace.Id);
        var privatePlacePublicChat1 = await CreateChat(true, "Private place public chat 1", privatePlace.Id);
        var privatePlacePublicChat2 = await CreateChat(true, "Private place public chat 2", privatePlace.Id);
        var publicPlace = await CreatePlace(true, "Bob's public Place");
        var publicPlacePrivateChat1 = await CreateChat(false, "Public place private chat 1", publicPlace.Id);
        var publicPlacePrivateChat2 = await CreateChat(false, "Public place private chat 2", publicPlace.Id);
        var publicPlacePublicChat1 = await CreateChat(true, "Public place public chat 1", publicPlace.Id);
        var publicPlacePublicChat2 = await CreateChat(true, "Public place public chat 2", publicPlace.Id);

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
        await _commander.Call(new SearchBackend_ChatContactBulkIndex(updates, ApiArray<IndexedChatContact>.Empty));
        await _commander.Call(new SearchBackend_Refresh(refreshPrivateChats: true, refreshPublicChats: true));

        // assert
        var searchResults = await Find(bob.Id, true, "chat");
        searchResults.Should()
            .BeEquivalentTo(
                new[] {
                    BuildSearchResult(bob.Id, publicChat1),
                    BuildSearchResult(bob.Id, publicChat2),
                    BuildSearchResult(bob.Id, publicPlacePublicChat1),
                    BuildSearchResult(bob.Id, publicPlacePublicChat2),
                }
            );

        searchResults = await Find(bob.Id, false, "chat");
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

    private async Task<Place> CreatePlace(bool isPublic, string title)
    {
        var (id, _) = await _tester.CreateChat(x => x with {
            Kind = ChatKind.Place,
            IsPublic = isPublic,
            Title = title,
        });
        return await _tester.Places.Get(_tester.Session, id.PlaceId, CancellationToken.None).Require();
    }

    private async Task<Chat.Chat> CreateChat(bool isPublic, string title, PlaceId placeId = default)
    {
        var (id, _) = await _tester.CreateChat(x => x with {
            Kind = null,
            Title = title,
            PlaceId = placeId,
            IsPublic = isPublic,
        });
        return await _tester.Chats.Get(_tester.Session, id, CancellationToken.None).Require();
    }

    private async Task<ApiArray<ContactSearchResult>> Find(UserId ownerId, bool isPublic, string criteria)
    {
        var searchResults = await _sut.FindChatContacts(ownerId,
            isPublic,
            criteria,
            0,
            20,
            CancellationToken.None);
        searchResults.Offset.Should().Be(0);
        return searchResults.Hits;
    }
}
