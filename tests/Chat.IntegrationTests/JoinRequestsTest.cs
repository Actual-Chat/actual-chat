using ActualChat.Chat.Db;
using ActualChat.Invite;
using ActualChat.Testing.Host;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class JoinRequestsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Owner => field ??= AppHost.NewWebClientTester(Out);
    private WebClientTester Moderator => field ??= AppHost.NewWebClientTester(Out);
    private WebClientTester Outsider => field ??= AppHost.NewWebClientTester(Out);
    private WebClientTester Guest => field ??= AppHost.NewWebClientTester(Out);

    protected override async Task DisposeAsync()
    {
        await Owner.DisposeSilentlyAsync();
        await Moderator.DisposeSilentlyAsync();
        await Outsider.DisposeSilentlyAsync();
        await Guest.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task SubmitShouldQueueOneRequestForPrivateChat()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        await Outsider.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(false);

        // act
        var request = await Submit(Outsider, chatId);
        var again = await Submit(Outsider, chatId);

        // assert
        request.Status.Should().Be(JoinRequestStatus.Pending);
        again.Id.Should().Be(request.Id, "a second submit returns the open request");
        var pending = await WaitForPending(Owner, chatId, 1);
        pending[0].Id.Should().Be(request.Id);
        var target = await GetTarget(Outsider, chatId);
        target!.State.Should().Be(JoinTargetState.RequestPending);
    }

    [Fact]
    public async Task WithdrawShouldCloseRequest()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        await Outsider.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(false);
        await Submit(Outsider, chatId);
        var withdrawCmd = new JoinRequests_Withdraw { Session = Outsider.Session, ChatId = chatId };

        // act
        await Outsider.Commander.Call(withdrawCmd);

        // assert
        await WaitForPending(Owner, chatId, 0);
        var target = await GetTarget(Outsider, chatId);
        target!.State.Should().Be(JoinTargetState.CanRequest);
    }

    [Fact]
    public async Task ApproveByModeratorShouldJoinAndJournal()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        var moderatorAccount = await Moderator.SignInAsUniqueAlice();
        var outsiderAccount = await Outsider.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(false);
        await MakeModerator(chatId, moderatorAccount.Id);
        var request = await Submit(Outsider, chatId);
        var approveCmd = new JoinRequests_Approve {
            Session = Moderator.Session,
            ChatId = chatId,
            RequestId = request.Id,
        };

        // act
        await Moderator.Commander.Call(approveCmd);

        // assert
        await Outsider.AssertJoined(chatId);
        var authorsBackend = Owner.AppServices.GetRequiredService<IAuthorsBackend>();
        var events = await authorsBackend.ListMembershipEvents(chatId, default);
        var outsiderEvents = events.Where(e => e.UserId == outsiderAccount.Id).ToList();
        outsiderEvents.Select(e => e.Kind).Should().BeEquivalentTo([
            MembershipEventKind.RequestSubmitted,
            MembershipEventKind.Joined,
            MembershipEventKind.RequestApproved,
        ]);
        outsiderEvents.Should().AllSatisfy(e => e.RequestId.Should().Be(request.Id));
        outsiderEvents.Single(e => e.Kind == MembershipEventKind.Joined)
            .Should().Match<MembershipEvent>(e =>
                e.Method == MembershipMethod.JoinRequest && e.ActorId == moderatorAccount.Id);
    }

    [Fact]
    public async Task PlainMemberShouldNotDecideAndRejectionShouldPause()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        var memberAccount = await Moderator.SignInAsUniqueAlice();
        await Outsider.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(false);
        await Owner.InviteToChat(chatId, memberAccount.Id);
        var request = await Submit(Outsider, chatId);
        var approveByMemberCmd = new JoinRequests_Approve {
            Session = Moderator.Session,
            ChatId = chatId,
            RequestId = request.Id,
        };
        var rejectCmd = new JoinRequests_Reject {
            Session = Owner.Session,
            ChatId = chatId,
            RequestId = request.Id,
        };

        // act
        var approveByMember = () => Moderator.Commander.Call(approveByMemberCmd);
        await approveByMember.Should().ThrowAsync<Exception>("deciding takes EditMembers");
        await Owner.Commander.Call(rejectCmd);
        var resubmit = () => Submit(Outsider, chatId);

        // assert
        await resubmit.Should().ThrowAsync<Exception>("a rejected request can't be repeated for a week");
        var target = await GetTarget(Outsider, chatId);
        target!.State.Should().Be(JoinTargetState.Rejected);
        target.RejectedUntil.Should().NotBeNull();
    }

    [Fact]
    public async Task ChatRequestInPrivatePlaceShouldWaitForPlace()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        var outsiderAccount = await Outsider.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(false, "requests place");
        var (chatId, _) = await Owner.CreateChat(false, "inner chat", place.Id);
        var rootChatId = place.Id.RootChatId;

        // act
        var target = await GetTarget(Outsider, chatId);
        var chatRequest = await Submit(Outsider, chatId);

        // assert
        target!.State.Should().Be(JoinTargetState.CanRequest);
        target.AlsoRequestsPlace.Should().BeTrue();
        chatRequest.Status.Should().Be(JoinRequestStatus.AwaitingPlace);
        await WaitForPending(Owner, chatId, 0);
        var placeRequests = await WaitForPending(Owner, rootChatId, 1);
        var approvePlaceCmd = new JoinRequests_Approve {
            Session = Owner.Session,
            ChatId = rootChatId,
            RequestId = placeRequests[0].Id,
        };

        // act
        await Owner.Commander.Call(approvePlaceCmd);

        // assert
        var chatPending = await WaitForPending(Owner, chatId, 1);
        chatPending[0].UserId.Should().Be(outsiderAccount.Id);
        chatPending[0].Id.Should().Be(chatRequest.Id);
    }

    [Fact]
    public async Task RejectedPlaceRequestShouldCloseChatRequest()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        var outsiderAccount = await Outsider.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(false, "requests place");
        var (chatId, _) = await Owner.CreateChat(false, "inner chat", place.Id);
        var rootChatId = place.Id.RootChatId;
        await Submit(Outsider, chatId);
        var placeRequests = await WaitForPending(Owner, rootChatId, 1);
        var rejectCmd = new JoinRequests_Reject {
            Session = Owner.Session,
            ChatId = rootChatId,
            RequestId = placeRequests[0].Id,
        };

        // act
        await Owner.Commander.Call(rejectCmd);

        // assert
        var backend = Owner.AppServices.GetRequiredService<IJoinRequestsBackend>();
        await TestWait.When(async ct => {
            var chatRequest = await backend.GetLatest(chatId, outsiderAccount.Id, ct);
            chatRequest!.Status.Should().Be(JoinRequestStatus.Rejected);
        });
    }

    [Fact]
    public async Task ChatRequestInPublicPlaceShouldJoinPlaceAtOnce()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        await Outsider.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(true, "public place");
        var (chatId, _) = await Owner.CreateChat(false, "inner chat", place.Id);

        // act
        var request = await Submit(Outsider, chatId);

        // assert
        request.Status.Should().Be(JoinRequestStatus.Pending);
        await Outsider.AssertJoined(place.Id.RootChatId);
        await WaitForPending(Owner, chatId, 1);
    }

    [Fact]
    public async Task JoinRequestAccessShouldDecideWhoSeesTarget()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        var memberAccount = await Moderator.SignInAsUniqueAlice();
        await Outsider.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(false, "access place", memberAccount);
        var (offChatId, _) = await Owner.CreateChat(false);
        var (anyoneChatId, _) = await Owner.CreateChat(false);
        var (placeMembersChatId, _) = await Owner.CreateChat(false, "inner chat", place.Id);
        var (signedInChatId, _) = await Owner.CreateChat(false);
        await ChangeAccess(offChatId, JoinRequestAccess.Off);
        await ChangeAccess(anyoneChatId, JoinRequestAccess.Anyone);
        await ChangeAccess(placeMembersChatId, JoinRequestAccess.PlaceMembers);

        // act
        var offTarget = await GetTarget(Outsider, offChatId);
        var submitToOff = () => Submit(Outsider, offChatId);
        var guestTarget = await GetTarget(Guest, anyoneChatId);
        var signedInOnlyTarget = await GetTarget(Guest, signedInChatId);
        var outsiderPlaceTarget = await GetTarget(Outsider, placeMembersChatId);
        var memberPlaceTarget = await GetTarget(Moderator, placeMembersChatId);

        // assert
        offTarget.Should().BeNull("Off looks the same as a missing chat");
        await submitToOff.Should().ThrowAsync<Exception>("an Off chat takes no requests");
        guestTarget!.State.Should().Be(JoinTargetState.SignInToRequest);
        signedInOnlyTarget.Should().BeNull("SignedIn hides the chat from guests");
        outsiderPlaceTarget.Should().BeNull("PlaceMembers hides the chat from outsiders");
        memberPlaceTarget!.State.Should().Be(JoinTargetState.CanRequest);
        memberPlaceTarget.AlsoRequestsPlace.Should().BeFalse();
    }

    [Fact]
    public async Task ExpiredRequestShouldLetUserAskAgain()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        await Outsider.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(false);
        var request = await Submit(Outsider, chatId);
        var dbHub = Owner.AppServices.GetRequiredService<DbHub<ChatDbContext>>();
        await using (var db = await dbHub.CreateDbContext(true)) {
            await db.JoinRequests
                .Where(r => r.Id == request.Id.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, DateTime.UtcNow.AddDays(-1)));
        }
        var backend = Owner.AppServices.GetRequiredService<IJoinRequestsBackend>();
        using (Invalidation.Begin())
            _ = backend.GetLatest(chatId, request.UserId, default);

        // act
        var expired = await backend.ListExpired(default, 100, default);
        var target = await GetTarget(Outsider, chatId);
        var newRequest = await Submit(Outsider, chatId);

        // assert
        expired.Should().Contain(r => r.Id == request.Id);
        target!.State.Should().Be(JoinTargetState.CanRequest, "an expired request can be filed again");
        newRequest.Id.Should().NotBe(request.Id);
        newRequest.Status.Should().Be(JoinRequestStatus.Pending);
        var closed = await backend.Get(chatId, request.Id, default);
        closed!.Status.Should().Be(JoinRequestStatus.Expired);
    }

    [Fact]
    public async Task InviteWithJoinShouldMakeMemberAtOnce()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        var outsiderAccount = await Outsider.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Owner.CreateChat(false);
        var useCmd = new Invites_Use { Session = Outsider.Session, InviteId = inviteId, Join = true };

        // act
        await Outsider.Commander.Call(useCmd);

        // assert
        await Outsider.AssertJoined(chatId);
        var authorsBackend = Owner.AppServices.GetRequiredService<IAuthorsBackend>();
        var events = await authorsBackend.ListMembershipEvents(chatId, default);
        events.Should().ContainSingle(e => e.UserId == outsiderAccount.Id)
            .Which.Should().Match<MembershipEvent>(e =>
                e.Method == MembershipMethod.InviteLink && e.InviteId == inviteId);
    }

    [Fact]
    public async Task MemberInviteToChatInPrivatePlaceShouldWaitForPlace()
    {
        // arrange
        var memberAccount = await Moderator.SignInAsUniqueAlice();
        await Owner.SignInAsUniqueBob();
        var outsiderAccount = await Outsider.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(false, "invite place", memberAccount);
        var (chatId, inviteId) = await Moderator.CreateChat(false, "member's chat", place.Id);
        var useCmd = new Invites_Use { Session = Outsider.Session, InviteId = inviteId, Join = true };

        // act
        await Outsider.Commander.Call(useCmd);
        var placeRequests = await WaitForPending(Owner, place.Id.RootChatId, 1);
        var approveCmd = new JoinRequests_Approve {
            Session = Owner.Session,
            ChatId = place.Id.RootChatId,
            RequestId = placeRequests[0].Id,
        };
        await Owner.Commander.Call(approveCmd);

        // assert
        placeRequests[0].UserId.Should().Be(outsiderAccount.Id);
        await Outsider.AssertJoined(chatId);
        await WaitForPending(Moderator, chatId, 0);
    }

    [Fact]
    public async Task PlaceOwnerInviteToChatShouldJoinPlaceToo()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        await Outsider.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(false, "invite place");
        var (chatId, inviteId) = await Owner.CreateChat(false, "owner's chat", place.Id);
        var useCmd = new Invites_Use { Session = Outsider.Session, InviteId = inviteId, Join = true };

        // act
        await Outsider.Commander.Call(useCmd);

        // assert
        await Outsider.AssertJoined(place.Id.RootChatId);
        await Outsider.AssertJoined(chatId);
    }

    [Fact]
    public async Task RequestAfterExclusionShouldBeMarked()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        var outsiderAccount = await Outsider.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(false);
        var author = await Owner.InviteToChat(chatId, outsiderAccount.Id);
        var excludeCmd = new Authors_Exclude { Session = Owner.Session, AuthorId = author.Id };
        await Owner.Commander.Call(excludeCmd);

        // act
        var request = await Submit(Outsider, chatId);

        // assert
        request.WasExcluded.Should().BeTrue();
    }

    // Private methods

    private static Task<JoinRequest> Submit(WebClientTester tester, ChatId chatId)
    {
        var submitCmd = new JoinRequests_Submit { Session = tester.Session, ChatId = chatId };
        return tester.Commander.Call(submitCmd);
    }

    private static Task<JoinTarget?> GetTarget(WebClientTester tester, ChatId chatId)
    {
        var joinRequests = tester.AppServices.GetRequiredService<IJoinRequests>();
        return joinRequests.GetTarget(tester.Session, chatId, default);
    }

    private static Task<ApiArray<JoinRequest>> WaitForPending(WebClientTester tester, ChatId chatId, int count)
    {
        var joinRequests = tester.AppServices.GetRequiredService<IJoinRequests>();
        return TestWait.When(async ct => {
            var pending = await joinRequests.ListPending(tester.Session, chatId, ct);
            pending.Should().HaveCount(count);
            return pending;
        });
    }

    private async Task MakeModerator(ChatId chatId, UserId userId)
    {
        var author = await Owner.InviteToChat(chatId, userId);
        var makeModeratorCmd = new Authors_ChangeRole {
            Session = Owner.Session,
            AuthorId = author.Id,
            SystemRole = SystemRole.Moderator,
            IsInRole = true,
        };
        await Owner.Commander.Call(makeModeratorCmd);
    }

    private Task<Chat> ChangeAccess(ChatId chatId, JoinRequestAccess access)
    {
        var changeCmd = new Chats_Change {
            Session = Owner.Session,
            ChatId = chatId,
            ExpectedVersion = null,
            Change = Change.Update(new ChatDiff { JoinRequestAccess = access }),
        };
        return Owner.Commander.Call(changeCmd);
    }
}
