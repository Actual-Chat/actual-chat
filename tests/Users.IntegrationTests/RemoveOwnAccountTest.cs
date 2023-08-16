using ActualChat.Chat;
using ActualChat.Testing.Host;
using Stl.Mathematics;

namespace ActualChat.Users.IntegrationTests;

public class RemoveOwnAccountTest : AppHostTestBase
{
    private ChatId TestChatId { get; } = new("the-actual-one");

    public RemoveOwnAccountTest(ITestOutputHelper @out) : base(@out)
    { }

    [Fact]
    public async Task DeleteOwnAccountTest()
    {
        using var appHost = await NewAppHost();
        await using var tester = appHost.NewWebClientTester();
        var services = tester.AppServices;
        var bob = await tester.SignIn(new User("Bob"));
        var session = tester.Session;

        var chats = services.GetRequiredService<IChats>();
        var createChatCommand = new Chats_Change(session,
            ChatId.None,
            null,
            new Change<ChatDiff> {
                Create = Option.Some(new ChatDiff {
                    Title = "TestChatToRemove",
                    IsPublic = false,
                    AllowGuestAuthors = true,
                    AllowAnonymousAuthors = true,
                    Kind = ChatKind.Group,
                }),
            });
        var chat = await services.Commander().Call(createChatCommand);
        chat.Should().NotBeNull();

        var entries = await CreateChatEntries(chats, session, chat.Id, 3);
        var entriesActual = await CreateChatEntries(chats, session, TestChatId, 3);
        var deleteOwnAccountCommand = new Accounts_DeleteOwn(session);
        await services.Commander().Call(deleteOwnAccountCommand);

        var chat1 = await chats.Get(session, chat.Id, CancellationToken.None);
        chat1.Should().BeNull();

        var lastEntryId = entries[^1].Id.LocalId;
        var idTileStack = Constants.Chat.IdTileStack;
        var idTile = idTileStack.GetOptimalCoveringTiles(new Range<long>(lastEntryId, lastEntryId))[^1];
        await FluentActions.Awaiting(() => chats.GetTile(session,
                chat.Id,
                ChatEntryKind.Text,
                idTile.Range,
                CancellationToken.None))
            .Should()
            .ThrowAsync<NotFoundException>();

        var lastActualEntryId = entriesActual[^1].Id.LocalId;
        var idTileActual = idTileStack.GetOptimalCoveringTiles(new Range<long>(lastActualEntryId, lastActualEntryId))[^1];
        await FluentActions.Awaiting(() => chats.GetTile(session,
                TestChatId,
                ChatEntryKind.Text,
                idTileActual.Range,
                CancellationToken.None))
            .Should()
            .ThrowAsync<NotFoundException>();
    }

    private async Task<ChatEntry[]> CreateChatEntries(
        IChats chats,
        Session session,
        ChatId chatId,
        int count)
    {
        var phrases = new[] {
            "back in black i hit the sack",
            "rape me rape me my friend",
            "it was a teenage wedding and the all folks wished them well",
        };
        var commander = chats.GetCommander();
        var entries = new List<ChatEntry>();

        while (true)
            foreach (var text in phrases) {
                if (count-- <= 0)
                    return entries.ToArray();

                var command = new Chats_UpsertTextEntry(session, chatId, null, text);
                var entry = await commander.Call(command, CancellationToken.None).ConfigureAwait(false);
                entries.Add(entry);
            }
    }
}
