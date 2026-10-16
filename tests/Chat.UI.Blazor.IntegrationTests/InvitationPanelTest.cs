using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class InvitationPanelTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ApprovedRequestShouldBeListedOnAllTab()
    {
        // arrange
        await using var owner = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueBob();
        await using var requester = AppHost.NewBlazorTester(Out);
        await requester.SignInAsUniqueAlice();
        var (chatId, _) = await owner.CreateChat(false);
        var submitCmd = new JoinRequests_Submit { Session = requester.Session, ChatId = chatId };
        var request = await requester.Commander.Call(submitCmd);
        var hub = requester.ScopedAppServices.AppUIHub();
        var settings = new ChatListSettings { FilterId = ChatListFilter.Unread.Id };

        // act
        var approveCmd = new JoinRequests_Approve { Session = owner.Session, ChatId = chatId, RequestId = request.Id };
        await owner.Commander.Call(approveCmd);
        await TestWait.When(async ct => {
            var invitation = await hub.NotificationsUI.GetInvitation(chatId, ct);
            invitation.Should().NotBeNull("an approval reaches the requester as an invitation");
            invitation!.Text.Should().NotBeEmpty();
        });
        await hub.ChatUI.MarkAsRead(chatId);

        // assert
        await TestWait.When(async ct => {
            var unreadState = await hub.ChatUI.GetUnreadState(chatId, ct);
            unreadState.Count.Value.Should().Be(0);
            var chats = await hub.ChatListUI.List(null, settings, ct);
            chats.Select(x => x.Id).Should().Contain(chatId, "the All tab lists the chat the requester was let into");
        });
    }

    [Fact]
    public async Task ApprovedPlaceRequestShouldBeListedOnWelcomeChat()
    {
        // arrange
        await using var owner = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueBob();
        await using var requester = AppHost.NewBlazorTester(Out);
        await requester.SignInAsUniqueAlice();
        var place = await owner.CreatePlace(false, "approval place");
        var (welcomeChatId, _) = await owner.CreateChat(true, "welcome", place.Id);
        var rootChatId = place.Id.RootChatId;
        var submitCmd = new JoinRequests_Submit { Session = requester.Session, ChatId = rootChatId };
        var request = await requester.Commander.Call(submitCmd);
        var hub = requester.ScopedAppServices.AppUIHub();
        var settings = new ChatListSettings { FilterId = ChatListFilter.Unread.Id };

        // act
        var approveCmd = new JoinRequests_Approve {
            Session = owner.Session,
            ChatId = rootChatId,
            RequestId = request.Id,
        };
        await owner.Commander.Call(approveCmd);

        // assert
        await TestWait.When(async ct => {
            var invitation = await hub.NotificationsUI.GetInvitation(welcomeChatId, ct);
            invitation.Should().NotBeNull("a place invitation is shown on its welcome chat");
            invitation!.Place!.Id.Should().Be(place.Id, "the row stands for the place, not its welcome chat");
            var chats = await hub.ChatListUI.List(null, settings, ct);
            chats.Select(x => x.Id).Should().Contain(welcomeChatId);
        });
    }

    [Fact]
    public async Task InvitationShouldBeDismissedOnceItsChatIsSeen()
    {
        // arrange
        await using var owner = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueBob();
        await using var requester = AppHost.NewBlazorTester(Out);
        await requester.SignInAsUniqueAlice();
        var (chatId, _) = await owner.CreateChat(false);
        var submitCmd = new JoinRequests_Submit { Session = requester.Session, ChatId = chatId };
        var request = await requester.Commander.Call(submitCmd);
        var approveCmd = new JoinRequests_Approve { Session = owner.Session, ChatId = chatId, RequestId = request.Id };
        await owner.Commander.Call(approveCmd);
        var hub = requester.ScopedAppServices.AppUIHub();
        await TestWait.When(async ct => {
            var invitation = await hub.NotificationsUI.GetInvitation(chatId, ct);
            invitation.Should().NotBeNull();
        });
        requester.ScopedAppServices.GetRequiredService<SeenNotificationDismisser>().Start();

        // act
        HashSet<ChatMessageKey> visibleKeys = [ChatMessageKey.New(ChatMessageKind.None, 1)];
        var visibility = new ChatViewItemVisibility(chatId, visibleKeys, true, true);
        hub.ChatUI.SelectChatOnNavigation(chatId);
        hub.ChatUI.ReportItemVisibility(visibility);

        // assert
        var settings = new ChatListSettings { FilterId = ChatListFilter.Unread.Id };
        await TestWait.When(async ct => {
            var invitation = await hub.NotificationsUI.GetInvitation(chatId, ct);
            invitation.Should().BeNull("having the chat open is seeing the invitation");
            var history = await hub.NotificationsUI.ListHistory(settings.FilterId, ct);
            history.Select(x => x.ChatId).Should().Contain(chatId, "a dismissed invitation moves to history");
        });
    }
}
