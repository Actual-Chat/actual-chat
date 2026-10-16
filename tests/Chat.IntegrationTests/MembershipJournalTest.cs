using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class MembershipJournalTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Owner => field ??= AppHost.NewWebClientTester(Out);
    private WebClientTester Moderator => field ??= AppHost.NewWebClientTester(Out);
    private WebClientTester Member => field ??= AppHost.NewWebClientTester(Out);

    protected override async Task DisposeAsync()
    {
        await Owner.DisposeSilentlyAsync();
        await Moderator.DisposeSilentlyAsync();
        await Member.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task JoinByInviteLinkShouldRecordInvite()
    {
        // arrange
        var ownerAccount = await Owner.SignInAsUniqueBob();
        var memberAccount = await Member.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Owner.CreateChat(false);

        // act
        await Member.JoinChat(chatId, inviteId);

        // assert
        var events = await WaitForEvents(chatId, 2);
        events[0].Should().Match<MembershipEvent>(e =>
            e.Kind == MembershipEventKind.Joined
            && e.Method == MembershipMethod.Creator
            && e.UserId == ownerAccount.Id);
        events[1].Should().Match<MembershipEvent>(e =>
            e.Kind == MembershipEventKind.Joined
            && e.Method == MembershipMethod.InviteLink
            && e.UserId == memberAccount.Id
            && e.ActorId == memberAccount.Id
            && e.InviteId == inviteId);
    }

    [Fact]
    public async Task JoinOfPublicChatShouldRecordSelf()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        var memberAccount = await Member.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);

        // act
        await Member.JoinChat(chatId, Symbol.Empty);

        // assert
        var events = await WaitForEvents(chatId, 2);
        events[1].Should().Match<MembershipEvent>(e =>
            e.Kind == MembershipEventKind.Joined
            && e.Method == MembershipMethod.Self
            && e.UserId == memberAccount.Id
            && e.InviteId.IsEmpty);
    }

    [Fact]
    public async Task AddExcludeAndRestoreShouldRecordWhoDidIt()
    {
        // arrange
        var ownerAccount = await Owner.SignInAsUniqueBob();
        var moderatorAccount = await Moderator.SignInAsUniqueAlice();
        var memberAccount = await Member.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(false);
        var moderatorAuthor = await Owner.InviteToChat(chatId, moderatorAccount.Id);
        var makeModeratorCmd = new Authors_ChangeRole {
            Session = Owner.Session,
            AuthorId = moderatorAuthor.Id,
            SystemRole = SystemRole.Moderator,
            IsInRole = true,
        };
        await Owner.Commander.Call(makeModeratorCmd);
        var memberAuthor = await Owner.InviteToChat(chatId, memberAccount.Id);
        var excludeCmd = new Authors_Exclude { Session = Moderator.Session, AuthorId = memberAuthor.Id };
        var restoreCmd = new Authors_Restore { Session = Moderator.Session, AuthorId = memberAuthor.Id };

        // act
        await Moderator.Commander.Call(excludeCmd);
        await Moderator.Commander.Call(restoreCmd);

        // assert
        var events = await WaitForEvents(chatId, 5);
        var memberEvents = events.Where(e => e.UserId == memberAccount.Id).ToList();
        memberEvents.Select(e => (e.Kind, e.Method, e.ActorId)).Should().Equal(
            (MembershipEventKind.Joined, MembershipMethod.Added, ownerAccount.Id),
            (MembershipEventKind.Left, MembershipMethod.Excluded, moderatorAccount.Id),
            (MembershipEventKind.Joined, MembershipMethod.Restored, moderatorAccount.Id));
        memberEvents.Should().AllSatisfy(e => e.AuthorId.Should().Be(memberAuthor.Id));
    }

    [Fact]
    public async Task LeaveShouldRecordSelf()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        var memberAccount = await Member.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        await Member.JoinChat(chatId, Symbol.Empty);
        var leaveCmd = new Authors_Leave { Session = Member.Session, ChatId = chatId };

        // act
        await Member.Commander.Call(leaveCmd);

        // assert
        var events = await WaitForEvents(chatId, 3);
        events[2].Should().Match<MembershipEvent>(e =>
            e.Kind == MembershipEventKind.Left
            && e.Method == MembershipMethod.Self
            && e.UserId == memberAccount.Id
            && e.ActorId == memberAccount.Id);
    }

    [Fact]
    public async Task LeavingPlaceShouldRecordPlaceLeftInItsChats()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        var memberAccount = await Member.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(false, "journal place", memberAccount);
        var (chatId, _) = await Owner.CreateChat(false, "private place chat", place.Id);
        await Owner.InviteToChat(chatId, memberAccount.Id);
        var leavePlaceCmd = new Places_Leave { Session = Member.Session, PlaceId = place.Id };

        // act
        await Member.Commander.Call(leavePlaceCmd);

        // assert
        var rootEvents = await WaitForEvents(place.Id.RootChatId, 3);
        rootEvents[2].Should().Match<MembershipEvent>(e =>
            e.Kind == MembershipEventKind.Left
            && e.Method == MembershipMethod.Self
            && e.UserId == memberAccount.Id);
        var chatEvents = await WaitForEvents(chatId, 3);
        chatEvents[2].Should().Match<MembershipEvent>(e =>
            e.Kind == MembershipEventKind.Left
            && e.Method == MembershipMethod.PlaceLeft
            && e.UserId == memberAccount.Id
            && e.ActorId == null);
    }

    // Private methods

    private Task<MembershipEvent[]> WaitForEvents(ChatId chatId, int count)
    {
        var authorsBackend = Owner.AppServices.GetRequiredService<IAuthorsBackend>();
        return TestWait.When(async ct => {
            var events = await authorsBackend.ListMembershipEvents(chatId, ct);
            events.Should().HaveCount(count, "every join and leave so far must be journaled");
            return events;
        });
    }
}
