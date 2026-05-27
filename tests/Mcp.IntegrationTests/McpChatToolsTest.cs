using System.Text.Json;
using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Mcp;
using ActualChat.Testing.Host;
using ModelContextProtocol.Client;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(McpCollection))]
public class McpChatToolsTest(McpCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : McpTestBase<McpCollection.AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ListGroupChats_ReturnsCreatedGroupChat()
    {
        var alice = await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true, title: "MyGroup");
        await WaitForContact(ContactId.NewAny(alice.Id, chatId));

        var client = await CreateClient();
        var result = await client.CallToolAsync("list_group_chats", new Dictionary<string, object?> {
            ["limit"] = 100,
        });
        var page = DeserializeResult<McpListChatsResult>(result);
        page.Chats.Should().Contain(c => c.Id == chatId.Value && c.Title == "MyGroup" && c.IsPublic);
    }

    [Fact]
    public async Task ListPlaces_AndListPlaceChats_RoundTrip()
    {
        var alice = await Tester.SignInAsUniqueAlice();
        var place = await Tester.CreatePlace(isPublicPlace: true, title: "MyPlace");
        await WaitForPlaceContact(place.Id);
        var (placeChatId, _) = await Tester.CreateChat(x => x with {
            IsPublic = true, Kind = null, Title = "InPlace", PlaceId = place.Id,
        });
        await WaitForContact(ContactId.NewAny(alice.Id, placeChatId));

        var client = await CreateClient();
        var placesResult = await client.CallToolAsync("list_places", new Dictionary<string, object?> {
            ["limit"] = 100,
        });
        var placesPage = DeserializeResult<McpListPlacesResult>(placesResult);
        placesPage.Places.Should().Contain(p => p.Id == place.Id.Value && p.Title == "MyPlace" && p.IsPublic);

        var chatsResult = await client.CallToolAsync("list_place_chats", new Dictionary<string, object?> {
            ["placeId"] = place.Id.Value,
            ["limit"] = 100,
        });
        var chatsPage = DeserializeResult<McpListChatsResult>(chatsResult);
        chatsPage.Chats.Should().Contain(c => c.Id == placeChatId.Value && c.Title == "InPlace");
    }

    [Fact]
    public async Task ListPeerChats_ReturnsPeer()
    {
        var alice = await Tester.SignInAsUniqueAlice();
        var bob = await Tester.SignInAsUniqueBob();
        var peerChatId = PeerChatId.New(alice.Id, bob.Id);

        await Tester.CreateTextEntry(peerChatId, "hi alice");
        await Tester.SignIn(alice);
        await Tester.CreateTextEntry(peerChatId, "hi bob");
        await WaitForContact(ContactId.NewUser(alice.Id, bob.Id));

        var client = await CreateClient();
        var result = await client.CallToolAsync("list_peer_chats", new Dictionary<string, object?> {
            ["limit"] = 100,
        });
        var page = DeserializeResult<McpListChatsResult>(result);
        page.Chats.Should().Contain(c => c.Id == peerChatId.Value);
    }

    private Task WaitForContact(ContactId contactId)
    {
        var contacts = Tester.AppServices.GetRequiredService<IContacts>();
        var placeId = contactId.ChatId is PlaceChatId placeChatId ? placeChatId.PlaceId : (PlaceId?)null;
        return ComputedTest.When(async ct => {
            var ids = await contacts.ListIds(Tester.Session, placeId, ct);
            ids.Should().Contain(contactId);
        }, WaitTimeout);
    }

    private Task WaitForPlaceContact(PlaceId placeId)
    {
        var contacts = Tester.AppServices.GetRequiredService<IContacts>();
        return ComputedTest.When(async ct => {
            var ids = await contacts.ListPlaceIds(Tester.Session, ct);
            ids.Should().Contain(placeId);
        }, WaitTimeout);
    }
}
