using ActualChat.Testing.Host;
using ModelContextProtocol.Client;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(McpCollection))]
public class McpToolSchemaTest(McpCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : McpTestBase<McpCollection.AppHostFixture>(fixture, @out)
{
    private static readonly string[] ExpectedTools = [
        "list_group_chats", "list_places", "list_place_chats", "list_peer_chats",
        "get_chat", "create_chat", "update_chat",
        "list_members", "add_members", "remove_member", "join_chat", "leave_chat",
        "create_invite_link", "list_invite_links", "revoke_invite_link",
        "get_place", "create_place", "update_place",
        "list_place_members", "add_place_members", "remove_place_member",
        "create_place_invite_link", "list_place_invite_links",
        "post_message", "edit_message", "remove_message", "get_id_range", "list_messages",
        "pin_message", "unpin_message", "list_pinned_messages",
        "react", "list_reactions", "notify_members", "notify_mentioned",
        "begin_upload", "append_upload", "finish_upload", "abort_upload", "upload_from_url",
        "list_media", "list_files", "list_links",
        "get_me", "list_avatars", "create_avatar", "update_avatar", "set_default_avatar",
        "list_conversations", "get_conversation",
        "search_messages", "search_contacts",
        "list_notifications",
    ];

    [Fact]
    public async Task ServerShouldExposeExactlyTheDocumentedTools()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var client = await CreateClient();

        // act
        var tools = await client.ListToolsAsync();

        // assert
        tools.Select(t => t.Name).Should().BeEquivalentTo(ExpectedTools);
    }
}
