using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class ChatUsagesTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task BasicTest()
    {
        // arrange
        var appHost = AppHost;
        var chatUsages = appHost.Services.GetRequiredService<IChatUsages>();
        await using var tester = appHost.NewWebClientTester(Out);
        _ = await tester.SignInAsUniqueAlice();
        var commander = tester.Commander;
        var session = tester.Session;
        var (chatId1, _) = await tester.CreateChat(true);
        var (chatId2, _) = await tester.CreateChat(true);

        // act
        var list1 = await chatUsages.GetRecencyList(session, ChatUsageListKind.PeerChatsWroteTo, default);

        // assert
        list1.Should().BeEmpty();

        // act
        await commander.Call(new ChatUsages_RegisterUsage(session, ChatUsageListKind.PeerChatsWroteTo, chatId1));
        list1 = await chatUsages.GetRecencyList(session, ChatUsageListKind.PeerChatsWroteTo, default);

        // assert
        list1.Should().HaveCount(1).And.Contain(chatId1);

        // act
        await commander.Call(new ChatUsages_RegisterUsage(session, ChatUsageListKind.PeerChatsWroteTo, chatId1));
        list1 = await chatUsages.GetRecencyList(session, ChatUsageListKind.PeerChatsWroteTo, default);

        // assert
        list1.Should().HaveCount(1).And.Contain(chatId1);

        // act
        await commander.Call(new ChatUsages_RegisterUsage(session, ChatUsageListKind.ViewedGroupChats, chatId2));
        list1 = await chatUsages.GetRecencyList(session, ChatUsageListKind.PeerChatsWroteTo, default);

        // assert
        list1.Should().HaveCount(1).And.Contain(chatId1);
        var list2 = await chatUsages.GetRecencyList(session, ChatUsageListKind.ViewedGroupChats, default);
        list2.Should().HaveCount(1).And.Contain(chatId2);

        // act
        await commander.Call(new ChatUsages_RegisterUsage(session, ChatUsageListKind.ViewedGroupChats, chatId1));
        list1 = await chatUsages.GetRecencyList(session, ChatUsageListKind.PeerChatsWroteTo, default);

        // assert
        list1.Should().HaveCount(1).And.Contain(chatId1);
        list2 = await chatUsages.GetRecencyList(session, ChatUsageListKind.ViewedGroupChats, default);
        list2.Should().HaveCount(2).And.BeEquivalentTo([chatId1, chatId2]);

        // arrange
        await using var tester2 = appHost.NewWebClientTester(Out);
        _ = await tester2.SignInAsUniqueBob();
        var commander2 = tester2.Commander;
        var session2 = tester2.Session;

        // act
        var list3 = await chatUsages.GetRecencyList(session2, ChatUsageListKind.PeerChatsWroteTo, default);

        // assert
        list3.Should().BeEmpty();

        // act
        await commander2.Call(new ChatUsages_RegisterUsage(session2, ChatUsageListKind.PeerChatsWroteTo, chatId1));
        list3 = await chatUsages.GetRecencyList(session2, ChatUsageListKind.PeerChatsWroteTo, default);

        // assert
        list3.Should().HaveCount(1).And.Contain(chatId1);
    }

    [Fact]
    public async Task PurgeRecencyListTest()
    {
        // arrange
        var appHost = AppHost;
        var chatUsages = appHost.Services.GetRequiredService<IChatUsages>();
        await using var tester = appHost.NewWebClientTester(Out);
        _ = await tester.SignInAsUniqueAlice();
        var commander = tester.Commander;
        var session = tester.Session;
        var (chatId1, _) = await tester.CreateChat(true);
        var (chatId2, _) = await tester.CreateChat(true);
        const ChatUsageListKind listKind = ChatUsageListKind.ViewedGroupChats;

        // act
        await commander.Call(new ChatUsages_RegisterUsage(session, listKind, chatId1));
        await commander.Call(new ChatUsages_RegisterUsage(session, listKind, chatId2));
        var list1 = await chatUsages.GetRecencyList(session, listKind, default);

        // assert
        list1.Should().HaveCount(2).And.BeEquivalentTo([chatId2, chatId1]);

        // act
        var account = await tester.Accounts.GetOwn(session, default);
        await commander.Call(new ChatUsagesBackend_PurgeRecencyList(account.Id, listKind, 1), default);
        list1 = await chatUsages.GetRecencyList(session, listKind, default);

        // assert
        list1.Should().HaveCount(1).And.BeEquivalentTo([chatId2]);
    }

    [Fact]
    public async Task IgnoresPositionForUnreadableChat()
    {
        // arrange
        var appHost = AppHost;
        var chatPositionsBackend = appHost.Services.GetRequiredService<IChatPositionsBackend>();
        await using var owner = appHost.NewWebClientTester(Out);
        _ = await owner.SignInAsUniqueAlice();
        var (unreadableChatId, _) = await owner.CreateChat(false);
        await using var reader = appHost.NewWebClientTester(Out);
        var account = await reader.SignInAsUniqueBob();
        var (readableChatId, _) = await reader.CreateChat(false);
        // Read positions are clamped to the chat's last entry, so this one must point at a real entry
        var readableEntry = await reader.CreateTextEntry(readableChatId, "Hi");
        var readablePosition = new ChatPosition(readableEntry.LocalId);
        var unreadablePosition = new ChatPosition(20);

        // act
        await reader.Commander.Call(
            new ChatPositions_Set(reader.Session, readableChatId, ChatPositionKind.Read, readablePosition));
        await reader.Commander.Call(
            new ChatPositions_Set(reader.Session, unreadableChatId, ChatPositionKind.Read, unreadablePosition));
        var storedReadable = await chatPositionsBackend.Get(
            account.Id, readableChatId, ChatPositionKind.Read, default);
        var storedUnreadable = await chatPositionsBackend.Get(
            account.Id, unreadableChatId, ChatPositionKind.Read, default);

        // assert
        storedReadable.EntryLid.Should().Be(readablePosition.EntryLid);
        storedUnreadable.EntryLid.Should().Be(0);
    }

    [Fact]
    public async Task PublicCommandRejectsHeardPosition()
    {
        // arrange
        var appHost = AppHost;
        await using var tester = appHost.NewWebClientTester(Out);
        _ = await tester.SignInAsUniqueAlice();
        var (chatId, _) = await tester.CreateChat(false);

        // act
        var act = () => tester.Commander.Call(
            new ChatPositions_Set(tester.Session, chatId, ChatPositionKind.Heard, new ChatPosition(10)));

        // assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task BackendClampsHeardMaxValue()
    {
        // arrange
        var appHost = AppHost;
        var chatPositionsBackend = appHost.Services.GetRequiredService<IChatPositionsBackend>();
        await using var tester = appHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var (chatId, _) = await tester.CreateChat(false);
        await tester.CreateTextEntry(chatId, "Hi");

        // act
        await tester.Commander.Call(new ChatPositionsBackend_Set(
            account.Id, chatId, ChatPositionKind.Heard, new ChatPosition(long.MaxValue)));
        var stored = await chatPositionsBackend.Get(account.Id, chatId, ChatPositionKind.Heard, default);

        // assert
        stored.EntryLid.Should().BeLessThan(long.MaxValue);
    }

    [Fact]
    public async Task IgnoresUsageForUnreadableChat()
    {
        // arrange
        var appHost = AppHost;
        var chatUsages = appHost.Services.GetRequiredService<IChatUsages>();
        await using var owner = appHost.NewWebClientTester(Out);
        _ = await owner.SignInAsUniqueAlice();
        var (unreadableChatId, _) = await owner.CreateChat(false);
        await using var reader = appHost.NewWebClientTester(Out);
        _ = await reader.SignInAsUniqueBob();
        var (readableChatId, _) = await reader.CreateChat(false);
        const ChatUsageListKind listKind = ChatUsageListKind.ViewedGroupChats;

        // act
        await reader.Commander.Call(new ChatUsages_RegisterUsage(reader.Session, listKind, readableChatId));
        await reader.Commander.Call(new ChatUsages_RegisterUsage(reader.Session, listKind, unreadableChatId));
        var recencyList = await chatUsages.GetRecencyList(reader.Session, listKind, default);

        // assert
        recencyList.Should().Equal(readableChatId);
    }

    [Fact]
    public async Task UsesServerTimeForUsage()
    {
        // arrange
        var appHost = AppHost;
        var chatUsages = appHost.Services.GetRequiredService<IChatUsages>();
        await using var tester = appHost.NewWebClientTester(Out);
        _ = await tester.SignInAsUniqueAlice();
        var (firstChatId, _) = await tester.CreateChat(false);
        var (secondChatId, _) = await tester.CreateChat(false);
        const ChatUsageListKind listKind = ChatUsageListKind.ViewedGroupChats;
        var futureAccessTime = DateTime.UtcNow.AddYears(1);

        // act
        await tester.Commander.Call(
            new ChatUsages_RegisterUsage(tester.Session, listKind, firstChatId, futureAccessTime));
        await Task.Delay(20);
        await tester.Commander.Call(new ChatUsages_RegisterUsage(tester.Session, listKind, secondChatId));
        var recencyList = await chatUsages.GetRecencyList(tester.Session, listKind, default);

        // assert
        recencyList.Should().Equal(secondChatId, firstChatId);
    }
}
