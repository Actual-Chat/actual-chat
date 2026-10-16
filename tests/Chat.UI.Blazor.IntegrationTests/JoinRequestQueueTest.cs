using ActualChat.Localization;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using Bunit;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class JoinRequestQueueTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task QueueShouldShowCountToOwner()
    {
        // arrange
        await using var owner = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueBob();
        await using var outsider = AppHost.NewBlazorTester(Out);
        await outsider.SignInAsUniqueAlice();
        var (chatId, _) = await owner.CreateChat(false);
        await Submit(outsider, chatId);
        var hub = owner.ScopedAppServices.AppUIHub();
        owner.RenderModalHost(hub);

        // act
        var cut = owner.Render<JoinRequestQueue>(p => p.Add(x => x.ChatId, chatId));

        // assert
        var title = hub.StringLocalizer.RightPanel_GroupTitle_Format(hub.StringLocalizer.JoinRequest_GroupTitle, 1);
        await TestWait.WhenRendered(cut, () => {
            cut.Markup.Should().Contain(title);
            cut.FindAll(".join-request-item").Should().ContainSingle();
        });
    }

    [Fact]
    public async Task QueueShouldBeHiddenWithoutEditMembers()
    {
        // arrange
        await using var owner = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueBob();
        await using var member = AppHost.NewBlazorTester(Out);
        var memberAccount = await member.SignInAsUniqueAlice();
        await using var outsider = AppHost.NewBlazorTester(Out);
        await outsider.SignInAsUniqueAlice();
        var (chatId, _) = await owner.CreateChat(false);
        await owner.InviteToChat(chatId, memberAccount.Id);
        await Submit(outsider, chatId);
        var ownerHub = owner.ScopedAppServices.AppUIHub();
        await TestWait.When(async ct => {
            var ownerGroups = await ownerHub.ChatListUI.ListJoinRequestGroups(null, ct);
            ownerGroups.Should().ContainSingle();
        });
        var hub = member.ScopedAppServices.AppUIHub();
        member.RenderModalHost(hub);

        // act
        var cut = member.Render<JoinRequestQueue>(p => p.Add(x => x.ChatId, chatId));
        var groups = await hub.ChatListUI.ListJoinRequestGroups(null, CancellationToken.None);

        // assert
        groups.Should().BeEmpty();
        await TestWait.WhenRendered(cut, () => cut.FindAll(".join-request-item").Should().BeEmpty());
    }

    [Fact]
    public async Task ApproveShouldAdmitRequester()
    {
        // arrange
        await using var owner = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueBob();
        await using var outsider = AppHost.NewBlazorTester(Out);
        await outsider.SignInAsUniqueAlice();
        var (chatId, _) = await owner.CreateChat(false);
        await Submit(outsider, chatId);
        var hub = owner.ScopedAppServices.AppUIHub();
        owner.RenderModalHost(hub);
        var cut = owner.Render<JoinRequestQueue>(p => p.Add(x => x.ChatId, chatId));
        await TestWait.WhenRendered(cut, () => cut.FindAll(".join-request-item").Should().ContainSingle());

        // act
        await cut.InvokeAsync(() => cut.Find(".join-request-item .c-approve").Click());

        // assert
        await TestWait.WhenRendered(cut, () => cut.FindAll(".join-request-item").Should().BeEmpty());
        var outsiderChats = outsider.AppServices.GetRequiredService<IChats>();
        await TestWait.When(async ct => {
            var chat = await outsiderChats.Get(outsider.Session, chatId, ct);
            chat!.Rules.IsMember().Should().BeTrue();
        });
    }

    [Fact]
    public async Task RejectShouldCloseRequest()
    {
        // arrange
        await using var owner = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueBob();
        await using var outsider = AppHost.NewBlazorTester(Out);
        await outsider.SignInAsUniqueAlice();
        var (chatId, _) = await owner.CreateChat(false);
        await Submit(outsider, chatId);
        var hub = owner.ScopedAppServices.AppUIHub();
        owner.RenderModalHost(hub);
        var cut = owner.Render<JoinRequestQueue>(p => p.Add(x => x.ChatId, chatId));
        await TestWait.WhenRendered(cut, () => cut.FindAll(".join-request-item").Should().ContainSingle());

        // act
        await cut.InvokeAsync(() => cut.Find(".join-request-item .c-reject").Click());

        // assert
        await TestWait.WhenRendered(cut, () => cut.FindAll(".join-request-item").Should().BeEmpty());
        var joinRequests = outsider.AppServices.GetRequiredService<IJoinRequests>();
        var target = await joinRequests.GetTarget(outsider.Session, chatId, CancellationToken.None);
        target!.State.Should().Be(JoinTargetState.Rejected);
    }

    [Fact]
    public async Task GroupsShouldIncludeOwnPlaceRequests()
    {
        // arrange
        await using var owner = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueBob();
        await using var outsider = AppHost.NewBlazorTester(Out);
        await outsider.SignInAsUniqueAlice();
        var place = await owner.CreatePlace(false, "requests place");
        var request = await Submit(outsider, place.Id.RootChatId);
        var hub = owner.ScopedAppServices.AppUIHub();

        // act
        var groups = await TestWait.When(async ct => {
            var result = await hub.ChatListUI.ListJoinRequestGroups(null, ct);
            result.Should().ContainSingle();
            return result;
        });
        var placeGroups = await hub.ChatListUI.ListJoinRequestGroups(place.Id, CancellationToken.None);

        // assert
        groups[0].Chat.Id.Should().Be(place.Id.RootChatId);
        groups[0].Requests.Should().ContainSingle().Which.Id.Should().Be(request.Id);
        placeGroups.Should().ContainSingle();
    }

    [Fact]
    public async Task ModeratorShouldChangeJoinRequestAccess()
    {
        // arrange
        await using var owner = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueBob();
        await using var moderator = AppHost.NewBlazorTester(Out);
        var moderatorAccount = await moderator.SignInAsUniqueAlice();
        var (chatId, _) = await owner.CreateChat(false);
        var author = await owner.InviteToChat(chatId, moderatorAccount.Id);
        var makeModeratorCmd = new Authors_ChangeRole {
            Session = owner.Session,
            AuthorId = author.Id,
            SystemRole = SystemRole.Moderator,
            IsInRole = true,
        };
        await owner.Commander.Call(makeModeratorCmd);
        var hub = moderator.ScopedAppServices.AppUIHub();
        await TestWait.When(async ct => {
            var moderatorChat = await hub.Chats.Get(moderator.Session, chatId, ct);
            moderatorChat!.Rules.CanEditProperties().Should().BeTrue();
        });
        var host = moderator.RenderModalHost(hub);
        await hub.ModalUI.Show(new JoinRequestAccessModal.Model(chatId));
        await TestWait.WhenRendered(host, () => host.FindComponent<JoinRequestAccessPicker>());

        // act
        var picker = host.FindComponent<JoinRequestAccessPicker>();
        await picker.InvokeAsync(() => picker.Instance.ValueChanged.InvokeAsync(JoinRequestAccess.Off));

        // assert
        var chats = owner.AppServices.GetRequiredService<IChats>();
        await TestWait.When(async ct => {
            var chat = await chats.Get(owner.Session, chatId, ct);
            chat!.JoinRequestAccess.Should().Be(JoinRequestAccess.Off);
        });
    }

    // Private methods

    private static Task<JoinRequest> Submit(BlazorTester tester, ChatId chatId)
    {
        var submitCmd = new JoinRequests_Submit { Session = tester.Session, ChatId = chatId };
        return tester.Commander.Call(submitCmd);
    }
}
