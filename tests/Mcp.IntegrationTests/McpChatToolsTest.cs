using ActualChat.Contacts;
using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(McpCollection))]
public class McpChatToolsTest(McpCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : McpTestBase<McpCollection.AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task CreateChatShouldSetTitleDescriptionAndPicture()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var client = await CreateClient();
        var (scopeChatId, _) = await Tester.CreateChat(isPublicChat: true);
        var picture = await Tester.CreateImageMedia(scopeChatId, "pic.png");

        // act
        var created = await CallTool<McpChatDetails>(client, "create_chat", new {
            title = "Agents lounge",
            isPublic = false,
            description = "Where agents hang out",
            pictureMediaId = picture.Id.Value,
        });
        var fetched = await CallTool<McpChatDetails>(client, "get_chat", new { chatId = created.Id });

        // assert
        fetched.Title.Should().Be("Agents lounge");
        fetched.Description.Should().Be("Where agents hang out");
        fetched.IsPublic.Should().BeFalse();
        fetched.Kind.Should().Be("Group");
        fetched.PictureUrl.Should().NotBeNullOrEmpty();
        fetched.MemberCount.Should().Be(1);
        fetched.Permissions.Should().Contain("Owner");
    }

    [Fact]
    public async Task UpdateChatShouldChangeOnlyGivenFields()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var client = await CreateClient();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true, title: "Before");

        // act
        var updated = await CallTool<McpChatDetails>(client, "update_chat", new {
            chatId = chatId.Value,
            description = "Now with a description",
        });

        // assert
        updated.Title.Should().Be("Before");
        updated.Description.Should().Be("Now with a description");
        updated.IsPublic.Should().BeTrue();
    }

    [Fact]
    public async Task AddMembersShouldListThemAndRemoveMemberShouldDropThem()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var aliceKey = await IssueApiKey("alice");
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignIn(alice);
        var (chatId, _) = await Tester.CreateChat(isPublicChat: false);
        var client = await CreateClientWithRawKey(aliceKey);

        // act
        await CallTool(client, "add_members", new { chatId = chatId.Value, userIds = new[] { bob.Id.Value } });
        var afterAdd = await WaitFor(
            () => CallTool<McpListMembersResult>(client, "list_members", new { chatId = chatId.Value }),
            r => r.Members.Length == 2);
        var bobMember = afterAdd.Members.Single(m => m.UserId == bob.Id.Value);
        await CallTool(client, "remove_member", new { chatId = chatId.Value, authorId = bobMember.AuthorId });
        var afterRemove = await WaitFor(
            () => CallTool<McpListMembersResult>(client, "list_members", new { chatId = chatId.Value }),
            r => r.Members.Length == 1);

        // assert
        afterAdd.Members.Should().HaveCount(2);
        afterAdd.Members.Single(m => m.UserId == alice.Id.Value).IsOwner.Should().BeTrue();
        bobMember.IsOwner.Should().BeFalse();
        bobMember.Name.Should().Be(bob.Avatar.Name);
        afterRemove.Members.Should().ContainSingle(m => m.UserId == alice.Id.Value);
    }

    [Fact]
    public async Task JoinAndLeaveChatShouldToggleMembership()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var aliceKey = await IssueApiKey("alice");
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        await Tester.SignInAsUniqueBob();
        var bobClient = await CreateClient();
        var aliceClient = await CreateClientWithRawKey(aliceKey);

        // act
        await CallTool(bobClient, "join_chat", new { chatId = chatId.Value });
        var joined = await WaitFor(
            () => CallTool<McpListMembersResult>(aliceClient, "list_members", new { chatId = chatId.Value }),
            r => r.Members.Length == 2);
        await CallTool(bobClient, "leave_chat", new { chatId = chatId.Value });
        var left = await WaitFor(
            () => CallTool<McpListMembersResult>(aliceClient, "list_members", new { chatId = chatId.Value }),
            r => r.Members.Length == 1);

        // assert
        joined.Members.Should().HaveCount(2);
        left.Members.Should().ContainSingle(m => m.UserId == alice.Id.Value);
    }

    [Fact]
    public async Task CreateInviteLinkShouldLetAnotherUserJoin()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var aliceKey = await IssueApiKey("alice");
        var (chatId, _) = await Tester.CreateChat(isPublicChat: false);
        var aliceClient = await CreateClientWithRawKey(aliceKey);

        // act
        var link = await CallTool<McpInviteLink>(aliceClient, "create_invite_link", new { chatId = chatId.Value });
        var links = await CallTool<McpInviteLink[]>(aliceClient, "list_invite_links", new { chatId = chatId.Value });
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.JoinChat(chatId, link.Id);
        await Tester.SignIn(alice);
        var members = await WaitFor(
            () => CallTool<McpListMembersResult>(aliceClient, "list_members", new { chatId = chatId.Value }),
            r => r.Members.Length == 2);

        // assert
        link.Url.Should().EndWith($"/join/{link.Id}");
        link.Remaining.Should().BePositive();
        links.Should().Contain(l => l.Id == link.Id);
        members.Members.Should().Contain(m => m.UserId == bob.Id.Value);
    }

    [Fact]
    public async Task RevokeInviteLinkShouldDropItFromActiveLinks()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: false);
        var client = await CreateClient();
        var link = await CallTool<McpInviteLink>(client, "create_invite_link", new { chatId = chatId.Value });

        // act
        await CallTool(client, "revoke_invite_link", new { inviteId = link.Id });
        var links = await WaitFor(
            () => CallTool<McpInviteLink[]>(client, "list_invite_links", new { chatId = chatId.Value }),
            r => r.All(l => l.Id != link.Id));

        // assert
        links.Should().NotContain(l => l.Id == link.Id, "list_invite_links returns only active links");
    }

    [Fact]
    public async Task CreateChatInPlaceShouldBePlaceChat()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var client = await CreateClient();
        var place = await Tester.CreatePlace(isPublicPlace: true, title: "Agent place");

        // act
        var created = await CallTool<McpChatDetails>(client, "create_chat", new {
            title = "In place",
            isPublic = true,
            placeId = place.Id.Value,
        });

        // assert
        created.PlaceId.Should().Be(place.Id.Value);
        created.Kind.Should().Be("Place");
    }

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
        return TestWait.When(async ct => {
            var ids = await contacts.ListIds(Tester.Session, placeId, ct);
            ids.Should().Contain(contactId);
        }, WaitTimeout);
    }

    private Task WaitForPlaceContact(PlaceId placeId)
    {
        var contacts = Tester.AppServices.GetRequiredService<IContacts>();
        return TestWait.When(async ct => {
            var ids = await contacts.ListPlaceIds(Tester.Session, ct);
            ids.Should().Contain(placeId);
        }, WaitTimeout);
    }
}
