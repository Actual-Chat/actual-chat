using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class ChatUsagesTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task BasicTest()
    {
        var appHost = AppHost;
        var chatUsages = appHost.Services.GetRequiredService<IChatUsages>();
        await using var tester = appHost.NewWebClientTester(Out);
        var commander = tester.Commander;
        var session = tester.Session;

        ApiArray<ChatId> list1;
        ApiArray<ChatId> list2;
        list1 = await chatUsages.GetRecencyList(session, ChatUsageListKind.PeerChatsWroteTo, default);
        list1.Should().BeEmpty();

        var chatId1 = new ChatId(Generate.Option);
        var chatId2 = new ChatId(Generate.Option);

        await commander.Call(new ChatUsages_RegisterUsage(session, ChatUsageListKind.PeerChatsWroteTo, chatId1));
        list1 = await chatUsages.GetRecencyList(session, ChatUsageListKind.PeerChatsWroteTo, default);
        list1.Should().HaveCount(1).And.Contain(chatId1);
        await commander.Call(new ChatUsages_RegisterUsage(session, ChatUsageListKind.PeerChatsWroteTo, chatId1));
        list1 = await chatUsages.GetRecencyList(session, ChatUsageListKind.PeerChatsWroteTo, default);
        list1.Should().HaveCount(1).And.Contain(chatId1);

        await commander.Call(new ChatUsages_RegisterUsage(session, ChatUsageListKind.ViewedGroupChats, chatId2));
        list1 = await chatUsages.GetRecencyList(session, ChatUsageListKind.PeerChatsWroteTo, default);
        list1.Should().HaveCount(1).And.Contain(chatId1);
        list2 = await chatUsages.GetRecencyList(session, ChatUsageListKind.ViewedGroupChats, default);
        list2.Should().HaveCount(1).And.Contain(chatId2);

        await commander.Call(new ChatUsages_RegisterUsage(session, ChatUsageListKind.ViewedGroupChats, chatId1));
        list1 = await chatUsages.GetRecencyList(session, ChatUsageListKind.PeerChatsWroteTo, default);
        list1.Should().HaveCount(1).And.Contain(chatId1);
        list2 = await chatUsages.GetRecencyList(session, ChatUsageListKind.ViewedGroupChats, default);
        list2.Should().HaveCount(2).And.BeEquivalentTo([chatId1, chatId2]);

        await using var tester2 = appHost.NewWebClientTester(Out);
        var commander2 = tester2.Commander;
        var session2 = tester2.Session;

        ApiArray<ChatId> list3;
        list3 = await chatUsages.GetRecencyList(session2, ChatUsageListKind.PeerChatsWroteTo, default);
        list3.Should().BeEmpty();
        await commander2.Call(new ChatUsages_RegisterUsage(session2, ChatUsageListKind.PeerChatsWroteTo, chatId1));
        list3 = await chatUsages.GetRecencyList(session2, ChatUsageListKind.PeerChatsWroteTo, default);
        list3.Should().HaveCount(1).And.Contain(chatId1);
    }

    [Fact]
    public async Task PurgeRecencyListTest()
    {
        var appHost = AppHost;
        var chatUsages = appHost.Services.GetRequiredService<IChatUsages>();
        await using var tester = appHost.NewWebClientTester(Out);
        var commander = tester.Commander;
        var session = tester.Session;

        var chatId1 = new ChatId(Generate.Option);
        var chatId2 = new ChatId(Generate.Option);
        const ChatUsageListKind listKind = ChatUsageListKind.ViewedGroupChats;

        await commander.Call(new ChatUsages_RegisterUsage(session, listKind, chatId1));
        await commander.Call(new ChatUsages_RegisterUsage(session, listKind, chatId2));
        var list1 = await chatUsages.GetRecencyList(session, listKind, default);
        list1.Should().HaveCount(2).And.BeEquivalentTo([chatId2, chatId1]);

        var account = await tester.Accounts.GetOwn(session, default);
        await commander.Call(new ChatUsagesBackend_PurgeRecencyList(account.Id, listKind, 1), default);

        list1 = await chatUsages.GetRecencyList(session, listKind, default);
        list1.Should().HaveCount(1).And.BeEquivalentTo([chatId2]);
    }
}
