using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ChatThreadOperationsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateThread(bool isPlaceChat)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var session = tester.Session;
        await tester.SignInAsUniqueBob();

        var services = tester.AppServices;
        var chats = services.GetRequiredService<IChats>();
        var commander = tester.Commander;
        CancellationToken cancellationToken = default;

        var isPrivateChat = !isPlaceChat;
        PlaceId? placeId = null;
        if (isPlaceChat) {
            var place = await tester.CreatePlace(false);
            placeId = place.Id;
        }
        var (parentChatId, _) = await tester.CreateChat(isPrivateChat, placeId: placeId);
        var parentChat = await chats.Get(session, parentChatId, cancellationToken).Require();
        var messages = new[] {
            "Hello!",
            "How are you?",
            "I am fine! Thanks.",
        };
        var parentChatEntries = await InsertEntries(commander, session, parentChat.Id, messages, cancellationToken);

        var entryIdsForThread = parentChatEntries.Where((c, i) => i is 0 or 2).Select(c => c.Id).ToArray();
        var chat = await CreateThreadChat(commander, chats, session, parentChat.Id, "Thread#1", entryIdsForThread, cancellationToken);

        var range = await chats.GetIdRange(session, chat.Id, cancellationToken);
        range.IsEmpty.Should().BeFalse();
        var idTiles = Constants.Chat.EntryIdTiles;
        var resultChatEntries = new List<ChatEntry>();
        foreach (var tileRange in idTiles.GetCoveringTiles(range)) {
            var tile = await chats.GetTile(session, chat.Id, tileRange.Range, cancellationToken);
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
            availableThreads[0].Should().Be(chat.Id);
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

        var entryIdsForThread = parentChatEntries.Where((c, i) => i is 0 or 2).Select(c => c.Id).ToArray();
        var chat1 = await CreateThreadChat(commander, chats, session, parentChat.Id, "Thread#1", entryIdsForThread, cancellationToken);

        var messages2 = new[] {
            "What are going to do tonight?",
            "We are doing BBQ tonight. Join to us!",
        };

        var threadChatEntries = await InsertEntries(commander, session, chat1.Id, messages2, cancellationToken);

        var entryIdsForThread2 = threadChatEntries.Select(c => c.Id).ToArray();
        var chat2 = await CreateThreadChat(commander, chats, session, chat1.Id, "Thread#2", entryIdsForThread2, cancellationToken);

        var range = await chats.GetIdRange(session, chat2.Id, cancellationToken);
        range.IsEmpty.Should().BeFalse();
        var idTiles = Constants.Chat.EntryIdTiles;
        var resultChatEntries = new List<ChatEntry>();
        foreach (var tileRange in idTiles.GetCoveringTiles(range)) {
            var tile = await chats.GetTile(session, chat2.Id, tileRange.Range, cancellationToken);
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
            availableThreads[0].Should().Be(chat2.Id);
            availableThreads[1].Should().Be(chat1.Id);

            var availableThreads2 = await chatThreads.ListIdsForChat(session, chat1.Id, cancellationToken);
            availableThreads2.Should().HaveCount(1);
            availableThreads2[0].Should().Be(chat2.Id);
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
        var entryIdsForThread = parentChatEntries.Where((c, i) => i is 0 or 2).Select(c => c.Id).ToArray();
        var threadChat = await CreateThreadChat(commander, chats, session, parentChat.Id, "Thread#1", entryIdsForThread, cancellationToken);

        await commander.Run(new Chats_Change {
            Session = session,
            ChatId = threadChat.Id,
            ExpectedVersion = null,
            Change = Change.Remove<ChatDiff>(),
        }, cancellationToken);

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
        var entryIdsForThread = parentChatEntries.Where((c, i) => i is 0 or 2).Select(c => c.Id).ToArray();
        var threadChat = await CreateThreadChat(commander, chats, session, parentChat.Id, "Thread#1", entryIdsForThread, cancellationToken);

        await commander.Run(new Chats_Change {
            Session = session,
            ChatId = parentChat.Id,
            ExpectedVersion = null,
            Change = Change.Remove<ChatDiff>(),
        }, cancellationToken);

        var parentChat1 = await chats.Get(session, parentChatId, cancellationToken);
        parentChat1.Should().BeNull();
        var threadChat1 = await chats.Get(session, threadChat.Id, cancellationToken);
        threadChat1.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThreadChatShouldHaveSameReadWritePermissionsAsParent(bool isPublicChat)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var session = tester.Session;
        await tester.SignInAsUniqueBob();

        var services = tester.AppServices;
        var chats = services.GetRequiredService<IChats>();
        var commander = tester.Commander;
        CancellationToken cancellationToken = default;

        var (parentChatId, _) = await tester.CreateChat(isPublicChat);
        var parentChat = await chats.Get(session, parentChatId, cancellationToken).Require();
        var messages = new[] {
            "Hello!",
            "How are you?",
            "I am fine! Thanks.",
        };
        var parentChatEntries = await InsertEntries(commander, session, parentChat.Id, messages, cancellationToken);
        var entryIdsForThread = parentChatEntries.Select(c => c.Id).ToArray();
        var threadChat = await CreateThreadChat(commander, chats, session, parentChat.Id, "Thread#1", entryIdsForThread, cancellationToken);

        await AssertThreadChatHasSameReadWritePermissionsAsParent(
            tester.AppServices.GetRequiredService<IChats>(),
            session,
            parentChat.Id,
            threadChat.Id,
            true,
            true
        );

        await using var tester2 = appHost.NewBlazorTester(Out);
        var session2 = tester2.Session;
        await tester2.SignInAsUniqueAlice();

        await AssertThreadChatHasSameReadWritePermissionsAsParent(
            tester2.AppServices.GetRequiredService<IChats>(),
            session2,
            parentChat.Id,
            threadChat.Id,
            isPublicChat,
            false
            );
    }

    private static async Task<Chat> CreateThreadChat(ICommander commander, IChats chats, Session session, ChatId parentChatId,
        string title, ChatEntryId[] entryIdsForThread, CancellationToken cancellationToken)
    {
        var chatThread = await commander.Call(new ChatThreads_Start {
            Session = session,
            ParentChatId = parentChatId,
            Title = title,
            Description = "",
            EntryIds = entryIdsForThread,
        }, cancellationToken);
        var chat = await chats.Get(session, chatThread.Id, cancellationToken);
        chat.Require();
        return chat;
    }

    private static async Task<List<ChatEntry>> InsertEntries(ICommander commander, Session session, ChatId chatId, string[] messages,
        CancellationToken cancellationToken)
    {
        var parentChatEntries = new List<ChatEntry>();
        foreach (var message in messages) {
            var cmd = new Chats_UpsertEntry { Session = session, ChatId = chatId, LocalId = null, Text = message };
            var chatEntry = await commander.Call(cmd, cancellationToken);
            parentChatEntries.Add(chatEntry);
        }
        return parentChatEntries;
    }

    private static async Task AssertThreadChatHasSameReadWritePermissionsAsParent(
        IChats chats,
        Session session,
        ChatId parentChatId,
        ChatId threadChatId,
        bool canReadParent,
        bool canWriteParent)
    {
        CancellationToken cancellationToken = default;
        var parentChat2 = await chats.Get(session, parentChatId, cancellationToken);
        var canReadParentChat = parentChat2 is not null;
        canReadParentChat.Should().Be(canReadParent);
        var canWriteToParentChat = parentChat2 is not null && parentChat2.Rules.Permissions.Has(ChatPermissions.Write);
        canWriteToParentChat.Should().Be(canWriteParent);
        var threadChat2 = await chats.Get(session, threadChatId, cancellationToken);
        var canReadThreadChat = threadChat2 is not null;
        canReadThreadChat.Should().Be(canReadParentChat);
        var canWriteToThreadChat = threadChat2 is not null && threadChat2.Rules.Permissions.Has(ChatPermissions.Write);
        canWriteToThreadChat.Should().Be(canWriteToParentChat);
        if (parentChat2 is null || threadChat2 is null)
            return;

        threadChat2.Rules.CanUpload().Should().Be(parentChat2.Rules.CanUpload());
        threadChat2.Rules.CanWriteAudio().Should().Be(parentChat2.Rules.CanWriteAudio());
        threadChat2.Rules.CanWriteVideo().Should().Be(parentChat2.Rules.CanWriteVideo());
        threadChat2.Rules.CanReadAudio().Should().Be(parentChat2.Rules.CanReadAudio());
        threadChat2.Rules.CanReadVideo().Should().Be(parentChat2.Rules.CanReadVideo());
    }
}
