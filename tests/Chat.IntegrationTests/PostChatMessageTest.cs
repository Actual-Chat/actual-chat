using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class PostChatMessageTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task PostMessage()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        _ = await tester.SignInAsBob();
        var session = tester.Session;
        var commander = tester.Commander;

        var (chatId, _) = await tester.CreateChat(true);

        var cmd = new Chats_UpsertEntry(session, chatId, null) { Text = "Hello!" };
        var chatEntry = await commander.Call(cmd);

        chatEntry.ChatId.Should().Be(chatId);
        chatEntry.Content.Should().Be(cmd.Text);
    }

    [Fact]
    public async Task RejectsOversizedEntry()
    {
        // arrange
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        _ = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        var realisticText = new string('a', 31_999);
        var oversizedText = new string('b', Constants.Chat.MaxEntryTextLength + 1);
        var realisticCommand = new Chats_UpsertEntry(tester.Session, chatId, null) { Text = realisticText };
        var oversizedCommand = new Chats_UpsertEntry(tester.Session, chatId, null) { Text = oversizedText };

        // act
        var realisticEntry = await tester.Commander.Call(realisticCommand);
        var error = await Record.ExceptionAsync(() => tester.Commander.Call(oversizedCommand));

        // assert
        realisticEntry.Content.Should().Be(realisticText);
        error.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task EditMessage()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        _ = await tester.SignInAsBob();
        var session = tester.Session;
        var commander = tester.Commander;

        var (chatId, _) = await tester.CreateChat(true);

        var cmd = new Chats_UpsertEntry(session, chatId, null) { Text = "Hello!" };
        var chatEntry = await commander.Call(cmd);

        var cmd2 = new Chats_UpsertEntry(session, chatId, chatEntry.LocalId) { Text = "EditedMessage" };
        var editedChatEntry = await commander.Call(cmd2);

        editedChatEntry.ChatId.Should().Be(chatId);
        editedChatEntry.LocalId.Should().Be(chatEntry.LocalId);
        editedChatEntry.Content.Should().Be(cmd2.Text);
    }

    [Fact]
    public async Task ReplyMessage()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        _ = await tester.SignInAsBob();
        var session = tester.Session;
        var commander = tester.Commander;

        var (chatId, _) = await tester.CreateChat(true);

        var cmd = new Chats_UpsertEntry(session, chatId, null) { Text = "Hello!" };
        var chatEntry = await commander.Call(cmd);

        var cmd2 = new Chats_UpsertEntry(session, chatId, null) { Text = "Reply",
            RepliedEntryLid = chatEntry.LocalId,
        };
        var replyChatEntry = await commander.Call(cmd2);

        replyChatEntry.ChatId.Should().Be(chatId);
        replyChatEntry.Content.Should().Be(cmd2.Text);
        replyChatEntry.RepliedEntryLid.Should().Be(chatEntry.LocalId);
    }

    [Fact]
    public async Task EditReplyMessage()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        _ = await tester.SignInAsBob();
        var session = tester.Session;
        var commander = tester.Commander;

        var (chatId, _) = await tester.CreateChat(true);

        var cmd = new Chats_UpsertEntry(session, chatId, null) { Text = "Hello!" };
        var chatEntry = await commander.Call(cmd);

        var cmd2 = new Chats_UpsertEntry(session, chatId, null) { Text = "Reply",
            RepliedEntryLid = chatEntry.LocalId,
        };
        var replyChatEntry = await commander.Call(cmd2);

        var cmd3 = new Chats_UpsertEntry(session, chatId, replyChatEntry.LocalId) { Text = "EditedReply" };
        var editedReplyChatEntry = await commander.Call(cmd3);

        editedReplyChatEntry.ChatId.Should().Be(chatId);
        editedReplyChatEntry.LocalId.Should().Be(replyChatEntry.LocalId);
        editedReplyChatEntry.Content.Should().Be(cmd3.Text);
        editedReplyChatEntry.RepliedEntryLid.Should().Be(chatEntry.LocalId);
    }

    [Fact]
    public async Task UpdateAttachments()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        _ = await tester.SignInAsBob();
        var session = tester.Session;
        var commander = tester.Commander;
        var chats = tester.AppServices.GetRequiredService<IChats>();

        var (chatId, _) = await tester.CreateChat(true);

        // Create 3 media files for attachments
        var media1 = await SaveTextFile(tester, chatId, "file1.txt");
        var media2 = await SaveTextFile(tester, chatId, "file2.txt");
        var media3 = await SaveTextFile(tester, chatId, "file3.txt");

        // Create an entry with 3 attachments
        var attachments = new[] {
            new ChatEntryAttachment { MediaId = media1 },
            new ChatEntryAttachment { MediaId = media2 },
            new ChatEntryAttachment { MediaId = media3 },
        };
        var createCmd = new Chats_UpsertEntry(session, chatId, null) { Text = "Message with 3 attachments",
            Attachments = attachments,
        };
        var chatEntry = await commander.Call(createCmd);

        chatEntry.ChatId.Should().Be(chatId);
        chatEntry.Content.Should().Be(createCmd.Text);

        // Get entry to verify attachments
        var entryId = ChatEntryId.New(chatId, chatEntry.LocalId);
        var entryWith3Attachments = await chats.GetEntry(session, entryId);
        entryWith3Attachments.Should().NotBeNull();
        entryWith3Attachments.Attachments.Should().HaveCount(3);
        entryWith3Attachments.Attachments.Select(a => a.MediaId).Should().BeEquivalentTo(new[] { media1, media2, media3 });

        // Update entry to 2 attachments (remove the third one)
        var updatedAttachments = new[] {
            new ChatEntryAttachment { MediaId = media1 },
            new ChatEntryAttachment { MediaId = media3 },
        };
        var updateCmd = new Chats_UpsertEntry(session, chatId, chatEntry.LocalId) { Text = "Message with 2 attachments",
            Attachments = updatedAttachments,
        };
        var updatedEntry = await commander.Call(updateCmd);

        updatedEntry.LocalId.Should().Be(chatEntry.LocalId);
        updatedEntry.Content.Should().Be(updateCmd.Text);

        // Get entry to verify attachments were updated
        var entryWith2Attachments = await chats.GetEntry(session, entryId);
        entryWith2Attachments.Should().NotBeNull();
        entryWith2Attachments.Attachments.Should().HaveCount(2);
        entryWith2Attachments.Attachments.Select(a => a.MediaId).Should().BeEquivalentTo(new[] { media1, media3 });
    }

    private static Task<MediaId> SaveTextFile(IWebTester tester, ChatId chatId, string fileName)
        => tester.SaveTextFile(chatId, fileName, $"Test content for {fileName}");
}
