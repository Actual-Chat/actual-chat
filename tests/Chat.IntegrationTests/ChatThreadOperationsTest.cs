using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ChatThreadOperationsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task CreateThread()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var session = tester.Session;
        await tester.SignInAsUniqueBob();

        var services = tester.AppServices;
        var chats = services.GetRequiredService<IChats>();
        var commander = tester.Commander;
        CancellationToken cancellationToken = default;

        var (parentChatId, _) = await tester.CreateChat(false);
        var parentChat = await chats.Get(session, parentChatId, cancellationToken).Require();
        var messages = new[] {
            "Hello!",
            "How are you?",
            "I am fine! Thanks.",
        };
        var parentChatEntries = await InsertEntries(commander, session, parentChat.Id, messages, cancellationToken);

        var entryIdsForThread = parentChatEntries.Where((c, i) => i is 0 or 2).Select(c => c.Id.ToTextEntryId()).ToApiArray();
        var chat = await CreateThreadChat(commander, chats, session, parentChat.Id, "Thread#1", entryIdsForThread, cancellationToken);

        var range = await chats.GetIdRange(session, chat.Id, ChatEntryKind.Text, cancellationToken);
        range.IsEmpty.Should().BeFalse();
        var tileStack = Constants.Chat.ViewIdTileStack;
        var resultChatEntries = new List<ChatEntry>();
        foreach (var tileRange in tileStack.GetOptimalCoveringTiles(range)) {
            var tile = await chats.GetTile(session, chat.Id, ChatEntryKind.Text, tileRange.Range, cancellationToken);
            resultChatEntries.AddRange(tile.Entries);
        }
        resultChatEntries.Count.Should().Be(2);
        resultChatEntries[0].AuthorId.LocalId.Should().Be(parentChatEntries[0].AuthorId.LocalId);
        resultChatEntries[0].Content.Should().Be(parentChatEntries[0].Content);
        resultChatEntries[1].AuthorId.LocalId.Should().Be(parentChatEntries[2].AuthorId.LocalId);
        resultChatEntries[1].Content.Should().Be(parentChatEntries[2].Content);

        var chatThreads = services.GetRequiredService<IChatThreads>();
        await TestExt.When(async () => {
            var availableThreads = await chatThreads.ListIdsForChat(session, parentChat.Id, cancellationToken);
            availableThreads.Should().HaveCount(1);
            availableThreads[0].Id.Should().Be(chat.Id);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task CreateChildThread()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var session = tester.Session;
        await tester.SignInAsUniqueBob();

        var services = tester.AppServices;
        var chats = services.GetRequiredService<IChats>();
        var commander = tester.Commander;
        CancellationToken cancellationToken = default;

        var (parentChatId, _) = await tester.CreateChat(false);
        var parentChat = await chats.Get(session, parentChatId, cancellationToken).Require();
        var messages = new[] {
            "Hello!",
            "How are you?",
            "I am fine! Thanks.",
        };
        var parentChatEntries = await InsertEntries(commander, session, parentChat.Id, messages, cancellationToken);

        var entryIdsForThread = parentChatEntries.Where((c, i) => i is 0 or 2).Select(c => c.Id.ToTextEntryId()).ToApiArray();
        var chat1 = await CreateThreadChat(commander, chats, session, parentChat.Id, "Thread#1", entryIdsForThread, cancellationToken);

        var messages2 = new[] {
            "What are going to do tonight?",
            "We are doing BBQ tonight. Join to us!",
        };

        var threadChatEntries = await InsertEntries(commander, session, chat1.Id, messages2, cancellationToken);

        var entryIdsForThread2 = threadChatEntries.Select(c => c.Id.ToTextEntryId()).ToApiArray();
        var chat2 = await CreateThreadChat(commander, chats, session, chat1.Id, "Thread#2", entryIdsForThread2, cancellationToken);

        var range = await chats.GetIdRange(session, chat2.Id, ChatEntryKind.Text, cancellationToken);
        range.IsEmpty.Should().BeFalse();
        var tileStack = Constants.Chat.ViewIdTileStack;
        var resultChatEntries = new List<ChatEntry>();
        foreach (var tileRange in tileStack.GetOptimalCoveringTiles(range)) {
            var tile = await chats.GetTile(session, chat2.Id, ChatEntryKind.Text, tileRange.Range, cancellationToken);
            resultChatEntries.AddRange(tile.Entries);
        }
        resultChatEntries.Count.Should().Be(2);
        resultChatEntries[0].AuthorId.LocalId.Should().Be(threadChatEntries[0].AuthorId.LocalId);
        resultChatEntries[0].Content.Should().Be(threadChatEntries[0].Content);
        resultChatEntries[1].AuthorId.LocalId.Should().Be(threadChatEntries[1].AuthorId.LocalId);
        resultChatEntries[1].Content.Should().Be(threadChatEntries[1].Content);

        var chatThreads = services.GetRequiredService<IChatThreads>();
        await TestExt.When(async () => {
            var availableThreads = await chatThreads.ListIdsForChat(session, parentChat.Id, cancellationToken);
            availableThreads.Should().HaveCount(2);
            availableThreads[0].Id.Should().Be(chat2.Id);
            availableThreads[1].Id.Should().Be(chat1.Id);

            var availableThreads2 = await chatThreads.ListIdsForChat(session, chat1.Id, cancellationToken);
            availableThreads2.Should().HaveCount(1);
            availableThreads2[0].Id.Should().Be(chat2.Id);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RemoveThreadChat()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var session = tester.Session;
        await tester.SignInAsUniqueBob();

        var services = tester.AppServices;
        var chats = services.GetRequiredService<IChats>();
        var commander = tester.Commander;
        CancellationToken cancellationToken = default;

        var (parentChatId, _) = await tester.CreateChat(false);
        var parentChat = await chats.Get(session, parentChatId, cancellationToken).Require();
        var messages = new[] {
            "Hello!",
            "How are you?",
            "I am fine! Thanks.",
        };
        var parentChatEntries = await InsertEntries(commander, session, parentChat.Id, messages, cancellationToken);
        var entryIdsForThread = parentChatEntries.Where((c, i) => i is 0 or 2).Select(c => c.Id.ToTextEntryId()).ToApiArray();
        var threadChat = await CreateThreadChat(commander, chats, session, parentChat.Id, "Thread#1", entryIdsForThread, cancellationToken);

        await commander.Run(new Chats_Change(session, threadChat.Id, null, Change.Remove<ChatDiff>()), cancellationToken);

        var parentChat1 = await chats.Get(session, parentChatId, cancellationToken);
        parentChat1.Should().NotBeNull();
        var threadChat1 = await chats.Get(session, threadChat.Id, cancellationToken);
        threadChat1.Should().BeNull();
    }

    [Fact]
    public async Task RemoveChatWithThread()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var session = tester.Session;
        await tester.SignInAsUniqueBob();

        var services = tester.AppServices;
        var chats = services.GetRequiredService<IChats>();
        var commander = tester.Commander;
        CancellationToken cancellationToken = default;

        var (parentChatId, _) = await tester.CreateChat(false);
        var parentChat = await chats.Get(session, parentChatId, cancellationToken).Require();
        var messages = new[] {
            "Hello!",
            "How are you?",
            "I am fine! Thanks.",
        };
        var parentChatEntries = await InsertEntries(commander, session, parentChat.Id, messages, cancellationToken);
        var entryIdsForThread = parentChatEntries.Where((c, i) => i is 0 or 2).Select(c => c.Id.ToTextEntryId()).ToApiArray();
        var threadChat = await CreateThreadChat(commander, chats, session, parentChat.Id, "Thread#1", entryIdsForThread, cancellationToken);

        await commander.Run(new Chats_Change(session, parentChat.Id, null, Change.Remove<ChatDiff>()), cancellationToken);

        var parentChat1 = await chats.Get(session, parentChatId, cancellationToken);
        parentChat1.Should().BeNull();
        var threadChat1 = await chats.Get(session, threadChat.Id, cancellationToken);
        threadChat1.Should().BeNull();
    }

    private static async Task<Chat> CreateThreadChat(ICommander commander, IChats chats, Session session, ChatId parentChatId,
        string title, ApiArray<TextEntryId> entryIdsForThread, CancellationToken cancellationToken)
    {
        var chatThread = await commander.Call(new ChatThreads_Start(session, parentChatId, title, "", entryIdsForThread), cancellationToken);
        var chat = await chats.Get(session, chatThread.Id, cancellationToken);
        chat.Require();
        return chat;
    }

    private static async Task<List<ChatEntry>> InsertEntries(ICommander commander, Session session, ChatId chatId, string[] messages,
        CancellationToken cancellationToken)
    {
        var parentChatEntries = new List<ChatEntry>();
        foreach (var message in messages) {
            var chatEntry = await commander.Call(new Chats_UpsertTextEntry(session, chatId, null, message), cancellationToken);
            parentChatEntries.Add(chatEntry);
        }
        return parentChatEntries;
    }
}
