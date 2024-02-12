using ActualChat.Chat;
using ActualChat.Testing.Host;
using ActualLab.Mathematics;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection)), Trait("Category", nameof(UserCollection))]
public class RemoveOwnAccountTest(AppHostFixture fixture, ITestOutputHelper @out)
    : AppHostTestBase<AppHostFixture>(fixture, @out)
{
    private ChatId TestChatId { get; } = new("the-actual-one");

    [Fact]
    public async Task DeleteOwnAccountTest()
    {
        var appHost = Host;
        await using var tester = appHost.NewWebClientTester(Out);
        var services = tester.AppServices;
        var bob = await tester.SignInAsBob();
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

        var lastEntryLid = entries[^1].LocalId;
        var idTileStack = Constants.Chat.ReaderIdTileStack;
        var idTile = idTileStack.GetOptimalCoveringTiles(new Range<long>(lastEntryLid, lastEntryLid + 1))[^1];
        await FluentActions.Awaiting(() => chats.GetTile(session,
                chat.Id,
                ChatEntryKind.Text,
                idTile.Range,
                CancellationToken.None))
            .Should()
            .ThrowAsync<NotFoundException>();

        var lastActualEntryId = entriesActual[^1].LocalId;
        var idTileActual = idTileStack.GetOptimalCoveringTiles(new Range<long>(lastActualEntryId, lastActualEntryId + 1))[^1];
        var tile = await chats.GetTile(session,
                TestChatId,
                ChatEntryKind.Text,
                idTileActual.Range,
                CancellationToken.None);
        tile.Entries.Should().NotContain(e => e.LocalId == lastActualEntryId);
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
