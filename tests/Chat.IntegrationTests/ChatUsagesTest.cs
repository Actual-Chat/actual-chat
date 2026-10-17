using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
[Obsolete("2026.10: Covers IChatUsages, which old clients still call.")]
public class ChatUsagesTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task LegacyRecencyListsShouldComeFromContacts()
    {
        // arrange
        var chatUsages = AppHost.Services.GetRequiredService<IChatUsages>();
        await using var tester = AppHost.NewBlazorTester(Out);
        var session = tester.Session;
        var bob = await tester.SignInAsUniqueBob();
        await using var tester2 = AppHost.NewBlazorTester(Out);
        var alice = await tester2.SignInAsUniqueAlice();
        var peerChatId = PeerChatId.New(bob.Id, alice.Id);
        var (groupChatId, _) = await tester.CreateChat(false);

        // act
        var cmd = new Chats_UpsertEntry { Session = session, ChatId = peerChatId, LocalId = null, Text = "Hello!" };
        await tester.Commander.Call(cmd);

        // assert
        await TestWait.When(async ct => {
            var peers = await chatUsages.GetLegacyRecencyList(session, ChatUsageListKind.PeerChatsWroteTo, ct);
            peers.Should().Equal(peerChatId);
            var groups = await chatUsages.GetLegacyRecencyList(session, ChatUsageListKind.ViewedGroupChats, ct);
            groups.Should().Contain(groupChatId).And.NotContain(peerChatId);
            var alicePeers = await chatUsages.GetLegacyRecencyList(
                tester2.Session, ChatUsageListKind.PeerChatsWroteTo, ct);
            alicePeers.Should().NotContain(peerChatId, "Alice never wrote to Bob");
        });
    }
}
