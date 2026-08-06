using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ChatUsagesTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task PostingMessageToPeerChatShouldUpdateRecencyList()
    {
        var appHost = AppHost;
        var chatUsages = appHost.Services.GetRequiredService<IChatUsages>();
        await using var tester = appHost.NewBlazorTester(Out);
        var session = tester.Session;
        var commander = tester.Commander;
        var account = await tester.SignInAsBob();

        await using var tester2 = appHost.NewBlazorTester(Out);
        var account2 = await tester2.SignInAsAlice();

        var peerChatId = PeerChatId.New(account.Id, account2.Id);
        var list = await chatUsages.GetRecencyList(session, ChatUsageListKind.PeerChatsWroteTo, default);
        list.Should().BeEmpty();

        var cmd = new Chats_UpsertEntry(session, peerChatId, null) { Text = "Hello!" };
        _ = await commander.Call(cmd);

        await ComputedTest.When(async _ => {
            list = await chatUsages.GetRecencyList(session, ChatUsageListKind.PeerChatsWroteTo, default);
            list.Should().HaveCount(1).And.Contain(peerChatId);
        });
    }
}
