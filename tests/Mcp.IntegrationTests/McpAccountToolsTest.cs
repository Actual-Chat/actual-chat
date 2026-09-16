using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(McpCollection))]
public class McpAccountToolsTest(McpCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : McpTestBase<McpCollection.AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task GetMeShouldReturnOwnAccount()
    {
        // arrange
        var account = await Tester.SignInAsUniqueAlice();
        var client = await CreateClient();

        // act
        var me = await CallTool<McpAccount>(client, "get_me");

        // assert
        me.UserId.Should().Be(account.Id.Value);
        me.Name.Should().Be(account.Avatar.Name);
        me.AvatarId.Should().Be(account.Avatar.Id.Value);
    }

    [Fact]
    public async Task CreateAvatarShouldAppearInListAndBeEditable()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var client = await CreateClient();

        // act
        var created = await CallTool<McpAvatar>(client, "create_avatar", new { name = "Agent", bio = "I post things" });
        var updated = await CallTool<McpAvatar>(client, "update_avatar",
            new { avatarId = created.Id, name = "Agent v2" });
        var avatars = await CallTool<McpAvatar[]>(client, "list_avatars");

        // assert
        created.Name.Should().Be("Agent");
        created.Bio.Should().Be("I post things");
        updated.Id.Should().Be(created.Id);
        updated.Name.Should().Be("Agent v2");
        updated.Bio.Should().Be("I post things", "an omitted field keeps its value");
        avatars.Should().Contain(a => a.Id == created.Id && a.Name == "Agent v2");
    }

    [Fact]
    public async Task SetDefaultAvatarShouldChangeGetMe()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var client = await CreateClient();
        var created = await CallTool<McpAvatar>(client, "create_avatar", new { name = "Default me" });

        // act
        await CallTool(client, "set_default_avatar", new { avatarId = created.Id });
        var me = await WaitFor(() => CallTool<McpAccount>(client, "get_me"), m => m.AvatarId == created.Id);
        var avatars = await CallTool<McpAvatar[]>(client, "list_avatars");

        // assert
        me.AvatarId.Should().Be(created.Id);
        avatars.Single(a => a.Id == created.Id).IsDefault.Should().BeTrue();
    }
}
