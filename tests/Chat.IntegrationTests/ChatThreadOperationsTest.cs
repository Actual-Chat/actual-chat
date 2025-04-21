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
        var parentChatEntries = new List<ChatEntry>();
        foreach (var message in messages) {
            var chatEntry = await commander.Call(new Chats_UpsertTextEntry(session, parentChat.Id, null, message), cancellationToken);
            parentChatEntries.Add(chatEntry);
        }

        var entryIdsForThread = parentChatEntries.Where((c, i) => i is 0 or 2).Select(c => c.Id.ToTextEntryId()).ToApiArray();
        var chatThread = await commander.Call(new ChatThreads_Start(session, parentChat.Id, "Thread#1", "Thread description", entryIdsForThread), cancellationToken);
        var chat = await chats.Get(session, chatThread.Id, cancellationToken);
        chat.Require();

        var range = await chats.GetIdRange(session, chat.Id, ChatEntryKind.Text, cancellationToken);
        range.IsEmpty.Should().BeFalse();
        var tileStack = Constants.Chat.ViewIdTileStack;
        var resultChatEntries = new List<ChatEntry>();
        foreach (var tileRange in tileStack.GetOptimalCoveringTiles(range)) {
            var tile = await chats.GetTile(session, chat.Id, ChatEntryKind.Text, tileRange.Range, cancellationToken);
            resultChatEntries.AddRange(tile.Entries);
        }
        resultChatEntries.Count.Should().Be(2);
        resultChatEntries[0].AuthorId.Should().Be(parentChatEntries[0].AuthorId);
        resultChatEntries[0].Content.Should().Be(parentChatEntries[0].Content);
        resultChatEntries[1].AuthorId.Should().Be(parentChatEntries[2].AuthorId);
        resultChatEntries[1].Content.Should().Be(parentChatEntries[2].Content);

        var chatThreads = services.GetRequiredService<IChatThreads>();
        await TestExt.When(async () => {
            var availableThreads = await chatThreads.ListIdsForChat(session, parentChat.Id, cancellationToken);
            availableThreads.Should().HaveCount(1);
            availableThreads[0].Id.Should().Be(chat.Id);
        }, TimeSpan.FromSeconds(10));
    }
}
