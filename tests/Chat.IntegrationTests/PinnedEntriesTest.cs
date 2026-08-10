using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class PinnedEntriesTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Owner => field ??= fixture.AppHost.NewWebClientTester(Out);
    private WebClientTester Moderator => field ??= fixture.AppHost.NewWebClientTester(Out);
    private WebClientTester Member => field ??= fixture.AppHost.NewWebClientTester(Out);

    protected override async Task DisposeAsync()
    {
        await Owner.DisposeSilentlyAsync();
        await Moderator.DisposeSilentlyAsync();
        await Member.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task OwnerAndModeratorCanPinButMemberCannot()
    {
        // arrange
        var (chatId, moderatorAuthorId, _) = await ArrangeChat();
        await PromoteToModerator(moderatorAuthorId);
        var entries = await Owner.CreateTextEntries(chatId, "msg", 3);

        // act
        await Owner.Commander.Call(new Chats_SetPinned(Owner.Session, entries[0].Id, true));
        await Moderator.Commander.Call(new Chats_SetPinned(Moderator.Session, entries[1].Id, true));
        var memberPin = () => Member.Commander.Call(new Chats_SetPinned(Member.Session, entries[2].Id, true));

        // assert
        await memberPin.Should().ThrowAsync<Exception>();
        var pins = await WaitPins(Owner, chatId, 2);
        pins.Should().Equal(entries[0].Id, entries[1].Id);
    }

    [Fact]
    public async Task PinsAreCappedAtThreeAndOrderedChronologically()
    {
        // arrange
        var (chatId, _, _) = await ArrangeChat();
        var e = await Owner.CreateTextEntries(chatId, "msg", 4);

        // act - pin out of order; the server sorts by message chronology
        await Owner.Commander.Call(new Chats_SetPinned(Owner.Session, e[2].Id, true));
        await Owner.Commander.Call(new Chats_SetPinned(Owner.Session, e[0].Id, true));
        await Owner.Commander.Call(new Chats_SetPinned(Owner.Session, e[3].Id, true));
        var fourth = () => Owner.Commander.Call(new Chats_SetPinned(Owner.Session, e[1].Id, true));

        // assert
        await fourth.Should().ThrowAsync<Exception>();
        var pins = await WaitPins(Owner, chatId, 3);
        pins.Should().Equal(e[0].Id, e[2].Id, e[3].Id);
    }

    [Fact]
    public async Task PinningARemovedEntryThrows()
    {
        // arrange
        var (chatId, _, _) = await ArrangeChat();
        var entry = await Owner.CreateTextEntry(chatId, "to be removed");
        await Owner.RemoveTextEntry(entry.Id);

        // act
        var pin = () => Owner.Commander.Call(new Chats_SetPinned(Owner.Session, entry.Id, true));

        // assert
        await pin.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task RemovedPinsAreDroppedOnTheNextChange()
    {
        // arrange - fill the cap, then delete one of the pinned messages
        var (chatId, _, _) = await ArrangeChat();
        var e = await Owner.CreateTextEntries(chatId, "msg", 4);
        await Owner.Commander.Call(new Chats_SetPinned(Owner.Session, e[0].Id, true));
        await Owner.Commander.Call(new Chats_SetPinned(Owner.Session, e[1].Id, true));
        await Owner.Commander.Call(new Chats_SetPinned(Owner.Session, e[2].Id, true));
        await Owner.RemoveTextEntry(e[0].Id);

        // act - the cap looks full, but the removed pin must not block a new one
        await Owner.Commander.Call(new Chats_SetPinned(Owner.Session, e[3].Id, true));

        // assert - stale id dropped, new one added
        var pins = await WaitPins(Owner, chatId, 3);
        pins.Should().Equal(e[1].Id, e[2].Id, e[3].Id);
    }

    [Fact]
    public async Task UnpinRemovesTheEntry()
    {
        // arrange
        var (chatId, _, _) = await ArrangeChat();
        var e = await Owner.CreateTextEntries(chatId, "msg", 2);
        await Owner.Commander.Call(new Chats_SetPinned(Owner.Session, e[0].Id, true));
        await Owner.Commander.Call(new Chats_SetPinned(Owner.Session, e[1].Id, true));

        // act
        await Owner.Commander.Call(new Chats_SetPinned(Owner.Session, e[0].Id, false));

        // assert
        var pins = await WaitPins(Owner, chatId, 1);
        pins.Should().Equal(e[1].Id);
    }

    [Fact]
    public async Task PinningTheSameEntryTwiceIsIdempotent()
    {
        // arrange
        var (chatId, _, _) = await ArrangeChat();
        var entry = await Owner.CreateTextEntry(chatId, "once");

        // act
        await Owner.Commander.Call(new Chats_SetPinned(Owner.Session, entry.Id, true));
        await Owner.Commander.Call(new Chats_SetPinned(Owner.Session, entry.Id, true));

        // assert
        var pins = await WaitPins(Owner, chatId, 1);
        pins.Should().Equal(entry.Id);
    }

    [Fact]
    public async Task PeerParticipantCanPin()
    {
        // arrange
        var alice = await Owner.SignInAsUniqueAlice();
        var bob = await Member.SignInAsUniqueBob();
        var chatId = (ChatId)PeerChatId.New(alice.Id, bob.Id);
        var entry = await Owner.CreateTextEntry(chatId, "peer message");

        // act
        await Owner.Commander.Call(new Chats_SetPinned(Owner.Session, entry.Id, true));

        // assert
        var pins = await WaitPins(Owner, chatId, 1);
        pins.Should().Equal(entry.Id);
    }

    // Private methods

    private async Task<(ChatId ChatId, AuthorId ModeratorAuthorId, AuthorId MemberAuthorId)> ArrangeChat()
    {
        await Owner.SignInAsUniqueBob();
        await Moderator.SignInAsUniqueAlice();
        await Member.SignInAsUniqueAlice();

        var (chatId, inviteId) = await Owner.CreateChat(false);
        var moderatorAuthor = await Moderator.JoinChat(chatId, inviteId);
        var memberAuthor = await Member.JoinChat(chatId, inviteId);
        return (chatId, moderatorAuthor.Id, memberAuthor.Id);
    }

    private Task PromoteToModerator(AuthorId authorId)
        => Owner.Commander.Call(new Authors_ChangeRole(Owner.Session, authorId, SystemRole.Moderator, true));

    private static async Task<ApiArray<ChatEntryId>> WaitPins(WebClientTester tester, ChatId chatId, int expectedCount)
    {
        // ListPinnedEntries is a consolidated compute method; settle on the expected size
        // instead of reading a possibly stale snapshot.
        var chats = tester.AppServices.GetRequiredService<IChats>();
        var computed = await Computed.Capture(() => chats.ListPinnedEntries(tester.Session, chatId, default));
        try {
            computed = await computed
                .When(x => x.Count == expectedCount)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException) { }

        return computed.Value;
    }
}
