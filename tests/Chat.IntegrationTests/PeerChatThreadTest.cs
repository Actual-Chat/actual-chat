using ActualChat.Chat.Db;
using ActualChat.Testing.Host;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class PeerChatThreadTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task PeerThreadIsReadableAndCreatesNoRole()
    {
        // arrange
        var appHost = AppHost;
        await using var aliceTester = appHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        await using var bobTester = appHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        // Bob adds Alice as a contact so Alice is allowed to start a thread in the peer chat.
        await bobTester.CreatePeerContact(bob, alice);

        var session = aliceTester.Session;
        var services = aliceTester.AppServices;
        var chats = services.GetRequiredService<IChats>();
        var chatThreads = services.GetRequiredService<IChatThreads>();
        var commander = aliceTester.Commander;
        var peerChatId = (ChatId)PeerChatId.New(alice.Id, bob.Id);
        CancellationToken cancellationToken = default;

        var entries = await InsertEntries(commander, session, peerChatId, ["Hello!", "How are you?"], cancellationToken);

        // act
        var thread = await commander
            .Call(new ChatThreads_Start {
                Session = session,
                ParentChatId = peerChatId,
                Title = "Peer thread",
                Description = "",
                EntryIds = entries.Select(e => e.Id).ToArray(),
            }, cancellationToken)
            .Require();

        // assert
        var threadChatId = thread.Id;
        threadChatId.Kind.Should().Be(ChatKind.Thread);
        threadChatId.GetThreadOutermostParentOrSelf().Should().Be(peerChatId);

        // The peer chat and its thread are both readable (the original lockout repro).
        (await chats.Get(session, peerChatId, cancellationToken)).Should().NotBeNull();
        (await chats.Get(session, threadChatId, cancellationToken)).Should().NotBeNull();
        _ = await chats.GetIdRange(session, peerChatId, cancellationToken); // Must not throw / NotFound
        var threadRange = await chats.GetIdRange(session, threadChatId, cancellationToken);
        threadRange.IsEmpty.Should().BeFalse();

        await TestExt.When(async () => {
            var threadIds = await chatThreads.ListIdsForChat(session, peerChatId, cancellationToken);
            threadIds.Select(x => (ChatId)x).Should().Contain(threadChatId);
        }, TimeSpan.FromSeconds(10));

        // The thread creator resolves without any role row (the "'Role' is not found" regression).
        var creator = await chatThreads.GetThreadCreator(session, (ThreadChatId)threadChatId, cancellationToken);
        creator.Should().NotBeNull();
        creator!.Avatar.Name.Should().NotBeNullOrEmpty();

        // No role row is persisted for the thread chat.
        var dbHub = services.DbHub<ChatDbContext>();
        await using var dbContext = await dbHub.CreateDbContext();
        var threadRolePrefix = threadChatId.Value + ":";
        var hasThreadRole = await dbContext.AuthorRoles
            .AnyAsync(r => r.DbRoleId.StartsWith(threadRolePrefix), cancellationToken);
        hasThreadRole.Should().BeFalse();
    }

    [Fact]
    public async Task RolesBackendRejectsPeerAndThreadChats()
    {
        // arrange
        var appHost = AppHost;
        await using var aliceTester = appHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        await using var bobTester = appHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();

        var commander = aliceTester.Commander;
        var peerChatId = (ChatId)PeerChatId.New(alice.Id, bob.Id);
        var threadChatId = (ChatId)peerChatId.CreateThreadId(1);
        var roleChange = new Change<RoleDiff> {
            Create = new RoleDiff {
                SystemRole = SystemRole.Owner,
                Permissions = ChatPermissions.Owner,
            },
        };

        // act / assert
        await FluentActions
            .Awaiting(() => commander.Call(new RolesBackend_Change(peerChatId, default, null, roleChange), CancellationToken.None))
            .Should().ThrowAsync<Exception>();
        await FluentActions
            .Awaiting(() => commander.Call(new RolesBackend_Change(threadChatId, default, null, roleChange), CancellationToken.None))
            .Should().ThrowAsync<Exception>();
    }

    private static async Task<List<ChatEntry>> InsertEntries(
        ICommander commander, Session session, ChatId chatId, string[] messages, CancellationToken cancellationToken)
    {
        var entries = new List<ChatEntry>();
        foreach (var message in messages) {
            var entry = await commander.Call(new Chats_UpsertEntry {
                Session = session,
                ChatId = chatId,
                LocalId = null,
                Text = message,
            }, cancellationToken);
            entries.Add(entry);
        }
        return entries;
    }
}
