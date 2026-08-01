using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(McpCollection))]
public class McpMessageToolsTest(McpCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : McpTestBase<McpCollection.AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task PostMessage_ReturnsLid_AndAppearsInTile()
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var client = await CreateClient();

        var result = await client.CallToolAsync("post_message", new Dictionary<string, object?> {
            ["chatId"] = chatId.Value,
            ["text"] = "hello from mcp",
        });
        var lid = DeserializeResult<long>(result);
        lid.Should().BeGreaterThan(0);

        var entry = await Tester.Chats.GetEntry(Tester.Session, ChatEntryId.New(chatId, lid));
        entry.Should().NotBeNull();
        entry!.Content.Should().Be("hello from mcp");
    }

    [Fact]
    public async Task EditMessage_UpdatesContent()
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var posted = await Tester.CreateTextEntry(chatId, "original");
        var client = await CreateClient();

        var result = await client.CallToolAsync("edit_message", new Dictionary<string, object?> {
            ["chatId"] = chatId.Value,
            ["entryId"] = posted.LocalId,
            ["text"] = "edited",
        });
        result.IsError.Should().NotBe(true);

        var entry = await Tester.Chats.GetEntry(Tester.Session, posted.Id);
        entry!.Content.Should().Be("edited");
    }

    [Fact]
    public async Task RemoveMessage_SoftRemovesEntry()
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var posted = await Tester.CreateTextEntry(chatId, "to be removed");
        var client = await CreateClient();

        var result = await client.CallToolAsync("remove_message", new Dictionary<string, object?> {
            ["chatId"] = chatId.Value,
            ["entryId"] = posted.LocalId,
        });
        result.IsError.Should().NotBe(true);

        var idTile = Constants.Chat.ServerIdTileStack.FirstLayer.GetTile(posted.LocalId);
        var tile = await Tester.Chats.GetTile(Tester.Session, chatId, idTile.Range, CancellationToken.None);
        tile.Entries.Should().NotContain(e => e.LocalId == posted.LocalId && !e.IsRemoved);
    }

    [Fact]
    public async Task GetIdRange_ReturnsInclusiveRange()
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var posted = await Tester.CreateTextEntries(chatId, "hi", 3);
        var client = await CreateClient();

        var result = await client.CallToolAsync("get_id_range", new Dictionary<string, object?> {
            ["chatId"] = chatId.Value,
        });
        var range = DeserializeResult<McpIdRange<long>>(result);
        range.FirstId.Should().BeLessThanOrEqualTo(posted[0].LocalId);
        range.LastId.Should().BeGreaterThanOrEqualTo(posted[^1].LocalId);
    }

    [Fact]
    public async Task ListMessages_FromBeginning_ReturnsAllEntriesIncludingFlags()
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var posted = await Tester.CreateTextEntries(chatId, "msg", 5);
        var client = await CreateClient();

        var result = await client.CallToolAsync("list_messages", new Dictionary<string, object?> {
            ["chatId"] = chatId.Value,
            ["afterId"] = null,
            ["limit"] = 100,
        });
        var page = DeserializeResult<McpListMessagesResult>(result);

        page.Messages.Should().HaveCountGreaterThanOrEqualTo(posted.Length);
        var textOnly = page.Messages.Where(m => !m.IsSystem).ToArray();
        textOnly.Select(m => m.Text).Should().Contain(posted.Select(e => e.Content));
        textOnly.Should().OnlyContain(m =>
            !m.IsStreaming &&
            !m.IsTranscribed &&
            !m.IsRemoved &&
            !string.IsNullOrEmpty(m.AuthorId));
        page.FullRange.FirstId.Should().BeLessThanOrEqualTo(posted[0].LocalId);
        page.FullRange.LastId.Should().BeGreaterThanOrEqualTo(posted[^1].LocalId);
    }

    [Fact]
    public async Task ListMessages_AfterId_SkipsPreviousEntries()
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var posted = await Tester.CreateTextEntries(chatId, "msg", 6);
        var client = await CreateClient();

        var result = await client.CallToolAsync("list_messages", new Dictionary<string, object?> {
            ["chatId"] = chatId.Value,
            ["afterId"] = posted[2].LocalId,
            ["limit"] = 100,
        });
        var page = DeserializeResult<McpListMessagesResult>(result);

        page.Messages.Should().NotContain(m => m.Id <= posted[2].LocalId);
        page.Messages.Should().Contain(m => m.Id == posted[3].LocalId);
    }

    [Fact]
    public async Task ListMessages_OmitsRemovedEntries()
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var entries = await Tester.CreateTextEntries(chatId, "msg", 3);
        await Tester.RemoveTextEntry(entries[1].Id);
        var client = await CreateClient();

        var result = await client.CallToolAsync("list_messages", new Dictionary<string, object?> {
            ["chatId"] = chatId.Value,
            ["afterId"] = null,
            ["limit"] = 100,
        });
        var page = DeserializeResult<McpListMessagesResult>(result);

        page.Messages.Should().NotContain(m => m.Id == entries[1].LocalId);
        page.Messages.Should().Contain(m => m.Id == entries[0].LocalId);
        page.Messages.Should().Contain(m => m.Id == entries[2].LocalId);
    }

    [Fact]
    public async Task ListMessages_IncludesImageAttachmentWithUrls()
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var media = await Tester.CreateImageMedia(chatId, "photo.png", "image/png", 800, 600);
        var entry = await Tester.CreateTextEntry(chatId, "with image", media.Id);
        var client = await CreateClient();

        var result = await client.CallToolAsync("list_messages", new Dictionary<string, object?> {
            ["chatId"] = chatId.Value,
            ["afterId"] = null,
            ["limit"] = 100,
        });
        var page = DeserializeResult<McpListMessagesResult>(result);

        var message = page.Messages.Single(m => m.Id == entry.LocalId);
        message.Attachments.Should().HaveCount(1);
        var attachment = message.Attachments[0];
        attachment.Kind.Should().Be("image");
        attachment.ContentType.Should().Be("image/png");
        attachment.FileName.Should().Be("photo.png");
        attachment.Width.Should().Be(800);
        attachment.Height.Should().Be(600);
        attachment.MediaId.Should().Be(media.Id.Value);
        attachment.Url.Should().Be(Tester.UrlMapper.ContentUrl(media.BlobId));
        if (Tester.UrlMapper.HasImageProxy)
            attachment.PreviewUrl.Should().NotBeNullOrEmpty();
        else
            attachment.PreviewUrl.Should().BeNull();
    }

    [Fact]
    public async Task ListMessages_TextOnlyMessage_HasNoAttachments()
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var entry = await Tester.CreateTextEntry(chatId, "just text");
        var client = await CreateClient();

        var result = await client.CallToolAsync("list_messages", new Dictionary<string, object?> {
            ["chatId"] = chatId.Value,
            ["afterId"] = null,
            ["limit"] = 100,
        });
        var page = DeserializeResult<McpListMessagesResult>(result);

        var message = page.Messages.Single(m => m.Id == entry.LocalId);
        message.Attachments.Should().BeEmpty();
    }
}
