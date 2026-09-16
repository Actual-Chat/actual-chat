using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(McpCollection))]
public class McpPlaceToolsTest(McpCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : McpTestBase<McpCollection.AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task CreateAndUpdatePlaceShouldRoundTripFields()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var client = await CreateClient();
        var (scopeChatId, _) = await Tester.CreateChat(isPublicChat: true);
        var picture = await Tester.CreateImageMedia(scopeChatId, "pic.png");
        var background = await Tester.CreateImageMedia(scopeChatId, "bg.png");

        // act
        var created = await CallTool<McpPlaceDetails>(client, "create_place", new {
            title = "Agent HQ",
            isPublic = false,
            description = "All agents welcome",
            pictureMediaId = picture.Id.Value,
        });
        var updated = await CallTool<McpPlaceDetails>(client, "update_place", new {
            placeId = created.Id,
            backgroundMediaId = background.Id.Value,
        });
        var fetched = await CallTool<McpPlaceDetails>(client, "get_place", new { placeId = created.Id });

        // assert
        created.Title.Should().Be("Agent HQ");
        created.Description.Should().Be("All agents welcome");
        created.PictureUrl.Should().NotBeNullOrEmpty();
        updated.BackgroundUrl.Should().NotBeNullOrEmpty();
        updated.Title.Should().Be("Agent HQ");
        fetched.MemberCount.Should().Be(1);
        fetched.Permissions.Should().Contain("Owner");
    }

    [Fact]
    public async Task PlaceMembersShouldBeAddedListedAndRemoved()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var aliceKey = await IssueApiKey("alice");
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignIn(alice);
        var place = await Tester.CreatePlace(isPublicPlace: false, title: "Members place");
        var client = await CreateClientWithRawKey(aliceKey);

        // act
        await CallTool(client, "add_place_members", new { placeId = place.Id.Value, userIds = new[] { bob.Id.Value } });
        var afterAdd = await WaitFor(
            () => CallTool<McpListMembersResult>(client, "list_place_members", new { placeId = place.Id.Value }),
            r => r.Members.Length == 2);
        var bobMember = afterAdd.Members.Single(m => m.UserId == bob.Id.Value);
        await CallTool(client, "remove_place_member", new { placeId = place.Id.Value, authorId = bobMember.AuthorId });
        var afterRemove = await WaitFor(
            () => CallTool<McpListMembersResult>(client, "list_place_members", new { placeId = place.Id.Value }),
            r => r.Members.Length == 1);

        // assert
        afterAdd.Members.Should().HaveCount(2);
        afterAdd.Members.Single(m => m.UserId == alice.Id.Value).IsOwner.Should().BeTrue();
        afterRemove.Members.Should().ContainSingle(m => m.UserId == alice.Id.Value);
    }

    [Fact]
    public async Task PlaceInviteLinkShouldBeCreatedListedAndRevoked()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var place = await Tester.CreatePlace(isPublicPlace: false, title: "Invite place");
        var client = await CreateClient();

        // act
        var link = await CallTool<McpInviteLink>(client, "create_place_invite_link", new { placeId = place.Id.Value });
        var links = await CallTool<McpInviteLink[]>(client, "list_place_invite_links",
            new { placeId = place.Id.Value });
        await CallTool(client, "revoke_invite_link", new { inviteId = link.Id });
        var afterRevoke = await WaitFor(
            () => CallTool<McpInviteLink[]>(client, "list_place_invite_links", new { placeId = place.Id.Value }),
            r => r.All(l => l.Id != link.Id));

        // assert
        link.Url.Should().EndWith($"/join/{link.Id}");
        links.Should().Contain(l => l.Id == link.Id);
        afterRevoke.Should().NotContain(l => l.Id == link.Id);
    }

    [Fact]
    public async Task SetUpPlaceSequenceShouldProduceChatsWithMembers()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var aliceKey = await IssueApiKey("alice");
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignIn(alice);
        var client = await CreateClientWithRawKey(aliceKey);

        // act
        var place = await CallTool<McpPlaceDetails>(client, "create_place",
            new { title = "Auto place", isPublic = true });
        var general = await CallTool<McpChatDetails>(client, "create_chat",
            new { title = "general", isPublic = true, placeId = place.Id });
        var random = await CallTool<McpChatDetails>(client, "create_chat",
            new { title = "random", isPublic = true, placeId = place.Id });
        await CallTool(client, "add_place_members", new { placeId = place.Id, userIds = new[] { bob.Id.Value } });
        var chats = await WaitFor(
            () => CallTool<McpListChatsResult>(client, "list_place_chats", new { placeId = place.Id }),
            r => r.Chats.Length >= 2);
        var members = await WaitFor(
            () => CallTool<McpListMembersResult>(client, "list_place_members", new { placeId = place.Id }),
            r => r.Members.Length == 2);

        // assert
        chats.Chats.Select(c => c.Id).Should().Contain([general.Id, random.Id]);
        members.Members.Select(m => m.UserId).Should().Contain(bob.Id.Value);
    }
}
