using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class PostChatMessageTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task PostMessage()
    {
        // arrange
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        _ = await tester.SignInAsBob();
        var session = tester.Session;
        var commander = tester.Commander;
        var (chatId, _) = await tester.CreateChat(true);

        // act
        var cmd = new Chats_UpsertEntry(session, chatId, null) { Text = "Hello!" };
        var chatEntry = await commander.Call(cmd);

        // assert
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
        // arrange
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        _ = await tester.SignInAsBob();
        var session = tester.Session;
        var commander = tester.Commander;
        var (chatId, _) = await tester.CreateChat(true);
        var cmd = new Chats_UpsertEntry(session, chatId, null) { Text = "Hello!" };
        var chatEntry = await commander.Call(cmd);

        // act
        var cmd2 = new Chats_UpsertEntry(session, chatId, chatEntry.LocalId) { Text = "EditedMessage" };
        var editedChatEntry = await commander.Call(cmd2);

        // assert
        editedChatEntry.ChatId.Should().Be(chatId);
        editedChatEntry.LocalId.Should().Be(chatEntry.LocalId);
        editedChatEntry.Content.Should().Be(cmd2.Text);
    }

    [Fact]
    public async Task ReplyMessage()
    {
        // arrange
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        _ = await tester.SignInAsBob();
        var session = tester.Session;
        var commander = tester.Commander;
        var (chatId, _) = await tester.CreateChat(true);
        var cmd = new Chats_UpsertEntry(session, chatId, null) { Text = "Hello!" };
        var chatEntry = await commander.Call(cmd);

        // act
        var cmd2 = new Chats_UpsertEntry(session, chatId, null) { Text = "Reply",
            RepliedEntryLid = chatEntry.LocalId,
        };
        var replyChatEntry = await commander.Call(cmd2);

        // assert
        replyChatEntry.ChatId.Should().Be(chatId);
        replyChatEntry.Content.Should().Be(cmd2.Text);
        replyChatEntry.RepliedEntryLid.Should().Be(chatEntry.LocalId);
    }

    [Fact]
    public async Task EditReplyMessage()
    {
        // arrange
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

        // act
        var cmd3 = new Chats_UpsertEntry(session, chatId, replyChatEntry.LocalId) { Text = "EditedReply" };
        var editedReplyChatEntry = await commander.Call(cmd3);

        // assert
        editedReplyChatEntry.ChatId.Should().Be(chatId);
        editedReplyChatEntry.LocalId.Should().Be(replyChatEntry.LocalId);
        editedReplyChatEntry.Content.Should().Be(cmd3.Text);
        editedReplyChatEntry.RepliedEntryLid.Should().Be(chatEntry.LocalId);
    }

    [Fact]
    public async Task UpdateAttachments()
    {
        // arrange
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        _ = await tester.SignInAsBob();
        var session = tester.Session;
        var commander = tester.Commander;
        var chats = tester.AppServices.GetRequiredService<IChats>();
        var (chatId, _) = await tester.CreateChat(true);
        var media1 = await SaveTextFile(tester, chatId, "file1.txt");
        var media2 = await SaveTextFile(tester, chatId, "file2.txt");
        var media3 = await SaveTextFile(tester, chatId, "file3.txt");

        // act
        var attachments = new[] {
            new ChatEntryAttachment { MediaId = media1 },
            new ChatEntryAttachment { MediaId = media2 },
            new ChatEntryAttachment { MediaId = media3 },
        };
        var createCmd = new Chats_UpsertEntry(session, chatId, null) { Text = "Message with 3 attachments",
            Attachments = attachments,
        };
        var chatEntry = await commander.Call(createCmd);

        // assert
        chatEntry.ChatId.Should().Be(chatId);
        chatEntry.Content.Should().Be(createCmd.Text);
        var entryId = ChatEntryId.New(chatId, chatEntry.LocalId);
        var entryWith3Attachments = await chats.GetEntry(session, entryId);
        entryWith3Attachments.Should().NotBeNull();
        entryWith3Attachments.Attachments.Should().HaveCount(3);
        entryWith3Attachments.Attachments.Select(a => a.MediaId).Should()
            .BeEquivalentTo(new[] { media1, media2, media3 });

        // act - update to 2 attachments (remove the second one)
        var updatedAttachments = new[] {
            new ChatEntryAttachment { MediaId = media1 },
            new ChatEntryAttachment { MediaId = media3 },
        };
        var updateCmd = new Chats_UpsertEntry(session, chatId, chatEntry.LocalId) { Text = "Message with 2 attachments",
            Attachments = updatedAttachments,
        };
        var updatedEntry = await commander.Call(updateCmd);

        // assert
        updatedEntry.LocalId.Should().Be(chatEntry.LocalId);
        updatedEntry.Content.Should().Be(updateCmd.Text);
        var entryWith2Attachments = await chats.GetEntry(session, entryId);
        entryWith2Attachments.Should().NotBeNull();
        entryWith2Attachments.Attachments.Should().HaveCount(2);
        entryWith2Attachments.Attachments.Select(a => a.MediaId).Should().BeEquivalentTo(new[] { media1, media3 });
    }

    [Fact]
    public async Task PostWhileCaughtUpAdvancesReadPosition()
    {
        // arrange
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        _ = await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var commander = tester.Commander;
        var chatPositions = tester.AppServices.GetRequiredService<IChatPositions>();
        var (chatId, _) = await tester.CreateChat(true);
        await CatchUp(tester, chatId);

        // act
        await commander.Call(new Chats_UpsertEntry(session, chatId, null) { Text = "First" });
        var entry2 = await commander.Call(new Chats_UpsertEntry(session, chatId, null) { Text = "Second" });

        // assert
        await ComputedTest.When(async ct => {
            var position = await chatPositions.GetOwn(session, chatId, ChatPositionKind.Read, ct);
            position.EntryLid.Should().Be(entry2.LocalId);
        });
    }

    [Fact]
    public async Task PostWhileBehindKeepsReadPosition()
    {
        // arrange
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var commander = tester.Commander;
        var chatPositions = tester.AppServices.GetRequiredService<IChatPositions>();
        var (chatId, _) = await tester.CreateChat(true);
        await commander.Call(new Chats_UpsertEntry(session, chatId, null) { Text = "First" });
        await commander.Call(new ChatPositionsBackend_Set(
            account.Id, chatId, ChatPositionKind.Read, new ChatPosition(0), Force: true));

        // act
        await commander.Call(new Chats_UpsertEntry(session, chatId, null) { Text = "Second" });

        // assert - the advance (if any) happens synchronously inside the command handler,
        // so the position is already final here
        var position = await chatPositions.GetOwn(session, chatId, ChatPositionKind.Read, CancellationToken.None);
        position.EntryLid.Should().Be(0);
    }

    [Fact]
    public async Task ForwardWhileCaughtUpAdvancesReadPosition()
    {
        // arrange
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        _ = await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var commander = tester.Commander;
        var chats = tester.AppServices.GetRequiredService<IChats>();
        var chatPositions = tester.AppServices.GetRequiredService<IChatPositions>();
        var (sourceChatId, _) = await tester.CreateChat(true);
        var (targetChatId, _) = await tester.CreateChat(true);
        await CatchUp(tester, targetChatId);
        var entry = await commander.Call(new Chats_UpsertEntry(session, sourceChatId, null) { Text = "To forward" });

        // act
        await commander.Call(new Chats_ForwardEntries(
            session, sourceChatId, [ChatEntryId.New(sourceChatId, entry.LocalId)], [targetChatId]));

        // assert
        await ComputedTest.When(async ct => {
            var targetNews = await chats.GetNews(session, targetChatId, ct);
            var lastLid = targetNews.TextEntryLidRange.End - 1;
            lastLid.Should().BeGreaterThan(0);
            var position = await chatPositions.GetOwn(session, targetChatId, ChatPositionKind.Read, ct);
            position.EntryLid.Should().Be(lastLid);
        });
    }

    private static Task<MediaId> SaveTextFile(IWebTester tester, ChatId chatId, string fileName)
        => tester.SaveTextFile(chatId, fileName, $"Test content for {fileName}");

    private async Task CatchUp(IWebTester tester, ChatId chatId)
    {
        // Simulates the client marking the chat fully read (what ChatView does when the end anchor is
        // visible). The chat's initial system entry is written asynchronously, so wait for it first -
        // otherwise the position is set to 0 and the author is "behind" the moment the entry lands.
        var chats = tester.AppServices.GetRequiredService<IChats>();
        var lastLid = 0L;
        await ComputedTest.When(async ct => {
            var news = await chats.GetNews(tester.Session, chatId, ct);
            lastLid = news.TextEntryLidRange.End - 1;
            lastLid.Should().BeGreaterThan(0);
        });
        Out.WriteLine($"CatchUp: chatId={chatId}, lastLid={lastLid}");
        await tester.Commander.Call(new ChatPositions_Set(
            tester.Session, chatId, ChatPositionKind.Read, new ChatPosition(lastLid)));
    }
}
