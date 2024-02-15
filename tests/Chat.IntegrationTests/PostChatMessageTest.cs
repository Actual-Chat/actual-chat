using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class PostChatMessageTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task PostMessage()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        _ = await tester.SignInAsBob();
        var session = tester.Session;
        var commander = tester.Commander;

        var (chatId, _) = await tester.CreateChat(true);

        var cmd = new Chats_UpsertTextEntry(session, chatId, null, "Hello!");
        var chatEntry = await commander.Call(cmd);

        chatEntry.ChatId.Should().Be(chatId);
        chatEntry.Content.Should().Be(cmd.Text);
    }

    [Fact]
    public async Task EditMessage()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        _ = await tester.SignInAsBob();
        var session = tester.Session;
        var commander = tester.Commander;

        var (chatId, _) = await tester.CreateChat(true);

        var cmd = new Chats_UpsertTextEntry(session, chatId, null, "Hello!");
        var chatEntry = await commander.Call(cmd);

        var cmd2 = new Chats_UpsertTextEntry(session, chatId, chatEntry.LocalId, "EditedMessage");
        var editedChatEntry = await commander.Call(cmd2);

        editedChatEntry.ChatId.Should().Be(chatId);
        editedChatEntry.LocalId.Should().Be(chatEntry.LocalId);
        editedChatEntry.Content.Should().Be(cmd2.Text);
    }

    [Fact]
    public async Task ReplyMessage()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        _ = await tester.SignInAsBob();
        var session = tester.Session;
        var commander = tester.Commander;

        var (chatId, _) = await tester.CreateChat(true);

        var cmd = new Chats_UpsertTextEntry(session, chatId, null, "Hello!");
        var chatEntry = await commander.Call(cmd);

        var cmd2 = new Chats_UpsertTextEntry(session, chatId, null, "Reply") {
            RepliedChatEntryId = chatEntry.LocalId
        };
        var replyChatEntry = await commander.Call(cmd2);

        replyChatEntry.ChatId.Should().Be(chatId);
        replyChatEntry.Content.Should().Be(cmd2.Text);
        replyChatEntry.RepliedEntryLocalId.Should().Be(chatEntry.LocalId);
    }

    [Fact]
    public async Task EditReplyMessage()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        _ = await tester.SignInAsBob();
        var session = tester.Session;
        var commander = tester.Commander;

        var (chatId, _) = await tester.CreateChat(true);

        var cmd = new Chats_UpsertTextEntry(session, chatId, null, "Hello!");
        var chatEntry = await commander.Call(cmd);

        var cmd2 = new Chats_UpsertTextEntry(session, chatId, null, "Reply") {
            RepliedChatEntryId = chatEntry.LocalId
        };
        var replyChatEntry = await commander.Call(cmd2);

        var cmd3 = new Chats_UpsertTextEntry(session, chatId, replyChatEntry.LocalId, "EditedReply");
        var editedReplyChatEntry = await commander.Call(cmd3);

        editedReplyChatEntry.ChatId.Should().Be(chatId);
        editedReplyChatEntry.LocalId.Should().Be(replyChatEntry.LocalId);
        editedReplyChatEntry.Content.Should().Be(cmd3.Text);
        editedReplyChatEntry.RepliedEntryLocalId.Should().Be(chatEntry.LocalId);
    }
}
