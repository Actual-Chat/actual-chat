using ActualChat.Testing.Host;

namespace ActualChat.Contacts.IntegrationTests;

[Collection(nameof(ContactCollection))]
public sealed class ContactsChatActivityTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private WebClientTester _bobTester = null!;
    private WebClientTester _aliceTester = null!;
    private IContacts _contacts = null!;
    private ICommander _commander = null!;

    // Protected/internal methods

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _bobTester = AppHost.NewWebClientTester(Out);
        _aliceTester = AppHost.NewWebClientTester(Out);
        _contacts = AppHost.Services.GetRequiredService<IContacts>();
        _commander = AppHost.Services.Commander();
    }

    protected override async Task DisposeAsync()
    {
        await _aliceTester.DisposeSilentlyAsync();
        await _bobTester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ListChatIdRangesShouldEndAtTheLastMessage()
    {
        // arrange
        await _bobTester.SignInAsUniqueBob();
        var (chatId, _) = await _bobTester.CreateChat(false);
        await AppHost.Services.WaitForOpeningEntry(chatId);

        // act
        var entry = await _bobTester.CreateTextEntry(chatId, "hello");

        // assert
        await TestWait.When(async ct => {
            var all = await _contacts.ListChatIdRanges(_bobTester.Session, ContactSubset.All(), ct);
            all.Should().ContainSingle(x => x.ChatId == chatId)
                .Which.LastEntryLid.Should().Be(entry.LocalId);

            var chats = await _contacts.ListChatIdRanges(_bobTester.Session, ContactSubset.Chats(), ct);
            chats.Should().Contain(x => x.ChatId == chatId);
        }, WaitTimeout);
    }

    [Fact]
    public async Task ListChatIdRangesShouldBeSortedByChatId()
    {
        // arrange
        await _bobTester.SignInAsUniqueBob();
        var chatIds = new List<ChatId>();
        for (var i = 0; i < 3; i++) {
            var (chatId, _) = await _bobTester.CreateChat(false);
            chatIds.Add(chatId);
        }

        // act
        var ranges = await TestWait.When(async ct => {
            var result = await _contacts.ListChatIdRanges(_bobTester.Session, ContactSubset.All(), ct);
            result.Select(x => x.ChatId).Should().Contain(chatIds);
            return result;
        }, WaitTimeout);

        // assert
        ranges.Select(x => x.ChatId.Value).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ListUnreadChatsShouldListAChatUntilItIsRead()
    {
        // arrange
        var alice = await _aliceTester.SignInAsUniqueAlice();
        await _bobTester.SignInAsUniqueBob();
        var (chatId, _) = await _bobTester.CreateChat(false);
        await AppHost.Services.WaitForOpeningEntry(chatId);
        var aliceAuthor = await _bobTester.InviteToChat(chatId, alice.Id);
        await _bobTester.WaitForJoinEntry(chatId, aliceAuthor.Id);
        var first = await _bobTester.CreateTextEntry(chatId, "first");
        var second = await _bobTester.CreateTextEntry(chatId, "second");
        var unreadChat = await TestWait.When(async ct => {
            var result = await _contacts.ListUnreadChats(_aliceTester.Session, ContactSubset.All(), ct);
            result.Should().ContainSingle(x => x.ChatId == chatId)
                .Which.LastEntryLid.Should().Be(second.LocalId);
            return result.Single(x => x.ChatId == chatId);
        }, WaitTimeout);

        // act
        var command = new ChatPositions_Set {
            Session = _aliceTester.Session,
            ChatId = chatId,
            Kind = ChatPositionKind.Read,
            Position = new ChatPosition(second.LocalId),
        };
        await _commander.Call(command);

        // assert
        unreadChat.ReadEntryLid.Should().BeLessThan(first.LocalId);
        unreadChat.UnreadCount.Should().Be(second.LocalId - unreadChat.ReadEntryLid);
        await TestWait.When(async ct => {
            var result = await _contacts.ListUnreadChats(_aliceTester.Session, ContactSubset.All(), ct);
            result.Should().NotContain(x => x.ChatId == chatId);
        }, WaitTimeout);
    }

    [Fact]
    public async Task ListUnreadChatsShouldNotListTheAuthorsOwnMessage()
    {
        // arrange
        await _bobTester.SignInAsUniqueBob();
        var (chatId, _) = await _bobTester.CreateChat(false);
        await AppHost.Services.WaitForOpeningEntry(chatId);
        var first = await _bobTester.CreateTextEntry(chatId, "first");
        var command = new ChatPositions_Set {
            Session = _bobTester.Session,
            ChatId = chatId,
            Kind = ChatPositionKind.Read,
            Position = new ChatPosition(first.LocalId),
        };
        await _commander.Call(command);

        // act
        var second = await _bobTester.CreateTextEntry(chatId, "second");

        // assert
        await TestWait.When(async ct => {
            var ranges = await _contacts.ListChatIdRanges(_bobTester.Session, ContactSubset.All(), ct);
            ranges.Should().Contain(x => x.ChatId == chatId && x.LastEntryLid == second.LocalId);

            var unreadChats = await _contacts.ListUnreadChats(_bobTester.Session, ContactSubset.All(), ct);
            unreadChats.Should().NotContain(x => x.ChatId == chatId);
        }, WaitTimeout);
    }
}
