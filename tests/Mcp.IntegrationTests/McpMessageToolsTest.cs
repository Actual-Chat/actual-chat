using ActualChat.External;
using ActualChat.Notifications;
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
    public async Task PostMessageWithReplyToShouldLinkEntries()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var original = await Tester.CreateTextEntry(chatId, "original");
        var client = await CreateClient();

        // act
        var lid = await CallTool<long>(client, "post_message",
            new { chatId = chatId.Value, text = "a reply", replyToId = original.LocalId });
        var listed = await CallTool<McpListMessagesResult>(client, "list_messages",
            new { chatId = chatId.Value, afterId = lid - 1 });

        // assert
        var entry = await Tester.Chats.GetEntry(Tester.Session, ChatEntryId.New(chatId, lid));
        entry!.RepliedEntryLid.Should().Be(original.LocalId);
        listed.Messages.Single(m => m.Id == lid).RepliedToId.Should().Be(original.LocalId);
    }

    [Fact]
    public async Task PostMessageWithAttachmentShouldCarryIt()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var media = await Tester.CreateImageMedia(chatId, "photo.png");
        var client = await CreateClient();

        // act
        var lid = await CallTool<long>(client, "post_message",
            new { chatId = chatId.Value, text = "with photo", attachmentMediaIds = new[] { media.Id.Value } });
        var listed = await CallTool<McpListMessagesResult>(client, "list_messages",
            new { chatId = chatId.Value, afterId = lid - 1 });

        // assert
        var message = listed.Messages.Single(m => m.Id == lid);
        message.Attachments.Should().ContainSingle().Which.MediaId.Should().Be(media.Id.Value);
    }

    [Fact]
    public async Task PinAndUnpinShouldUpdatePinnedList()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var entry = await Tester.CreateTextEntry(chatId, "pin me");
        var client = await CreateClient();

        // act
        await CallTool(client, "pin_message", new { chatId = chatId.Value, entryId = entry.LocalId });
        var pinned = await WaitFor(
            () => CallTool<ExternalMessage[]>(client, "list_pinned_messages", new { chatId = chatId.Value }),
            r => r.Length == 1);
        await CallTool(client, "unpin_message", new { chatId = chatId.Value, entryId = entry.LocalId });
        var unpinned = await WaitFor(
            () => CallTool<ExternalMessage[]>(client, "list_pinned_messages", new { chatId = chatId.Value }),
            r => r.Length == 0);

        // assert
        pinned.Should().ContainSingle().Which.Text.Should().Be("pin me");
        unpinned.Should().BeEmpty();
    }

    [Fact]
    public async Task ReactShouldToggleReaction()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var entry = await Tester.CreateTextEntry(chatId, "react to me");
        var client = await CreateClient();
        var args = new { chatId = chatId.Value, entryId = entry.LocalId, emoji = "👍" };

        // act
        await CallTool(client, "react", args);
        var reacted = await WaitFor(
            () => CallTool<McpReactionSummary[]>(client, "list_reactions", args),
            r => r.Length == 1);
        await CallTool(client, "react", args);
        var removed = await WaitFor(
            () => CallTool<McpReactionSummary[]>(client, "list_reactions", args),
            r => r.Length == 0);

        // assert
        var summary = reacted.Should().ContainSingle().Which;
        summary.Emoji.Should().Be("👍");
        summary.Count.Should().Be(1);
        removed.Should().BeEmpty();
    }

    [Fact]
    public async Task ReactWithUnknownEmojiShouldFail()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var entry = await Tester.CreateTextEntry(chatId, "x");
        var client = await CreateClient();

        // act
        var error = await CallToolExpectingError(client, "react",
            new { chatId = chatId.Value, entryId = entry.LocalId, emoji = "not-an-emoji" });

        // assert
        error.Should().NotBeEmpty();
    }

    [Fact]
    public async Task NotifyMembersShouldPostNotifyEntry()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: false);
        var client = await CreateClient();
        var before = await Tester.Chats.GetIdRange(Tester.Session, chatId, CancellationToken.None);

        // act
        await CallTool(client, "notify_members", new { chatId = chatId.Value });

        // assert
        var after = await WaitFor(
            () => Tester.Chats.GetIdRange(Tester.Session, chatId, CancellationToken.None),
            r => r.End > before.End);
        after.End.Should().BeGreaterThan(before.End, "notifying members writes a system entry");
        var lastEntryId = ChatEntryId.New(chatId, after.End - 1);
        var lastEntry = await Tester.Chats.GetEntry(Tester.Session, lastEntryId);
        lastEntry.Should().BeOfType<NotifyMembersEntry>();
    }

    [Fact]
    public async Task NotifyMentionedShouldMarkEntryNotified()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var aliceKey = await IssueApiKey("alice");
        var bob = await Tester.SignInAsUniqueBob();
        await Tester.SignIn(alice);
        var (chatId, _) = await Tester.CreateChat(isPublicChat: false);
        await Tester.InviteToChat(chatId, bob.Id);
        var entry = await Tester.CreateTextEntry(chatId, $"hey @u:{bob.Id} !");
        var client = await CreateClientWithRawKey(aliceKey);

        // act
        await CallTool(client, "notify_mentioned", new { chatId = chatId.Value, entryId = entry.LocalId });

        // assert
        var notifications = Tester.AppServices.GetRequiredService<INotifications>();
        var isNotified = await WaitFor(
            () => notifications.HasNotifiedMentionedMembers(Tester.Session, entry.Id, CancellationToken.None),
            x => x);
        isNotified.Should().BeTrue();
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
    public async Task PostMessageShouldMarkEntryAsSentViaApi()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var typed = await Tester.CreateTextEntry(chatId, "typed by hand");
        var client = await CreateClient();

        // act
        var result = await client.CallToolAsync("post_message", new Dictionary<string, object?> {
            ["chatId"] = chatId.Value,
            ["text"] = "posted by an agent",
        });
        var lid = DeserializeResult<long>(result);

        // assert
        var posted = await Tester.Chats.GetEntry(Tester.Session, ChatEntryId.New(chatId, lid));
        posted!.IsViaApi.Should().BeTrue();
        typed.IsViaApi.Should().BeFalse("the entry came from a regular session");
    }

    [Fact]
    public async Task EditMessageShouldMarkTypedEntryAsSentViaApi()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var typed = await Tester.CreateTextEntry(chatId, "typed by hand");
        var client = await CreateClient();

        // act
        var result = await client.CallToolAsync("edit_message", new Dictionary<string, object?> {
            ["chatId"] = chatId.Value,
            ["entryId"] = typed.LocalId,
            ["text"] = "rewritten by an agent",
        });

        // assert
        result.IsError.Should().NotBe(true);
        var entry = await Tester.Chats.GetEntry(Tester.Session, typed.Id);
        entry!.IsViaApi.Should().BeTrue();
    }

    [Fact]
    public async Task HandEditShouldKeepSentViaApiMark()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var client = await CreateClient();
        var result = await client.CallToolAsync("post_message", new Dictionary<string, object?> {
            ["chatId"] = chatId.Value,
            ["text"] = "posted by an agent",
        });
        var entryId = ChatEntryId.New(chatId, DeserializeResult<long>(result));

        // act
        var edited = await Tester.UpdateTextEntry(entryId, "touched up by hand");

        // assert
        edited.Content.Should().Be("touched up by hand");
        edited.IsViaApi.Should().BeTrue("a hand edit must not hide that an agent wrote the text");
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

        var idTile = Constants.Chat.EntryIdTiles.GetTile(posted.LocalId);
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
            !string.IsNullOrEmpty(m.Author.Id));
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
