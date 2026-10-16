using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public sealed class JoinRequestPushTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);

    [Fact]
    public async Task DecidersShouldShareOneNotificationClearedOnceNoneIsLeft()
    {
        // arrange
        var (chatId, owner, moderator) = await ArrangeModeratedChat();
        var first = await Tester.SignInAsUniqueAlice();
        var firstRequest = await Submit(chatId);
        var second = await Tester.SignInAsUniqueAlice();
        var secondRequest = await Submit(chatId);

        // act
        await WaitForJoinRequestNotification(owner.Id, chatId, 2);
        await WaitForJoinRequestNotification(moderator.Id, chatId, 2);
        await Tester.SignIn(owner);
        await Decide(chatId, firstRequest.Id, isApproved: true);
        await WaitForJoinRequestNotification(moderator.Id, chatId, 1);
        await Decide(chatId, secondRequest.Id, isApproved: false);

        // assert
        await WaitForJoinRequestNotification(owner.Id, chatId, 0);
        await WaitForJoinRequestNotification(moderator.Id, chatId, 0);
        await WaitForApprovalNotification(first.Id, chatId, isExpected: true);
        await WaitForApprovalNotification(second.Id, chatId, isExpected: false);
    }

    // Private methods

    private async Task<(ChatId ChatId, AccountFull Owner, AccountFull Moderator)> ArrangeModeratedChat()
    {
        var moderator = await Tester.SignInAsUniqueAlice();
        var owner = await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(false, "Join request push chat");
        var moderatorAuthor = await Tester.InviteToChat(chatId, moderator.Id);
        var makeModeratorCmd = new Authors_ChangeRole {
            Session = Tester.Session,
            AuthorId = moderatorAuthor.Id,
            SystemRole = SystemRole.Moderator,
            IsInRole = true,
        };
        await Tester.Commander.Call(makeModeratorCmd);
        return (chatId, owner, moderator);
    }

    private Task<JoinRequest> Submit(ChatId chatId)
    {
        var submitCmd = new JoinRequests_Submit { Session = Tester.Session, ChatId = chatId };
        return Tester.Commander.Call(submitCmd);
    }

    private Task Decide(ChatId chatId, JoinRequestId requestId, bool isApproved)
    {
        ICommand decideCmd = isApproved
            ? new JoinRequests_Approve { Session = Tester.Session, ChatId = chatId, RequestId = requestId }
            : new JoinRequests_Reject { Session = Tester.Session, ChatId = chatId, RequestId = requestId };
        return Tester.Commander.Call(decideCmd);
    }

    private Task WaitForJoinRequestNotification(UserId userId, ChatId chatId, int pendingCount)
        => TestWait.WhenPolled(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(userId, CancellationToken.None);
            var notifications = info.Items.OfType<JoinRequestNotification>().Where(n => n.ChatId == chatId).ToList();
            if (pendingCount == 0) {
                notifications.Should().BeEmpty("a decided queue leaves nothing to decide");
                return;
            }

            notifications.Should().ContainSingle("pending requests share one notification per chat")
                .Which.PendingCount.Should().Be(pendingCount);
        }, TimeSpan.FromSeconds(10));

    private Task WaitForApprovalNotification(UserId userId, ChatId chatId, bool isExpected)
        => TestWait.WhenPolled(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(userId, CancellationToken.None);
            var approvals = info.Items.OfType<InvitationNotification>().Where(n => n.ChatId == chatId);
            if (isExpected)
                approvals.Should().ContainSingle("the requester learns they were let in");
            else
                approvals.Should().BeEmpty("a rejection is silent");
        }, TimeSpan.FromSeconds(10));
}
