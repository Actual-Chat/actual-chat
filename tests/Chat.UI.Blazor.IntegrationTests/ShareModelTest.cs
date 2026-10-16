using ActualChat.Invite;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class ShareModelTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ChatInPlaceWithRequestsOffShouldBeSharedToPlaceMembersOnly()
    {
        // arrange
        await using var owner = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueBob();
        var place = await owner.CreatePlace(false, "share place");
        await SetPlaceAccess(owner, place, JoinRequestAccess.Off);
        var (chatId, _) = await owner.CreateChat(false, placeId: place.Id);
        var hub = owner.ScopedAppServices.AppUIHub();

        // act
        var model = await TestWait.When(async ct => {
            var result = await hub.ShareUI.GetModel(chatId, ct);
            result!.SelectorPrefs.Should().NotBeNull();
            return result;
        });

        // assert
        model.SelectorPrefs.Should().Be(new ShareWithPlaceMembersOnly(place.Id));
        model.IsApprovalNeeded.Should().BeTrue();
        model.Request.Link.Should().Be(Links.Chat(chatId));
    }

    [Fact]
    public async Task ChatInPlaceWithRequestsOnShouldBeSharedToAnyone()
    {
        // arrange
        await using var owner = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueBob();
        var place = await owner.CreatePlace(false, "share place");
        var (chatId, _) = await owner.CreateChat(false, placeId: place.Id);
        var hub = owner.ScopedAppServices.AppUIHub();

        // act
        var model = await hub.ShareUI.GetModel(chatId, CancellationToken.None);

        // assert
        model.Should().NotBeNull();
        model!.SelectorPrefs.Should().BeNull();
        model.IsApprovalNeeded.Should().BeTrue();
        model.InviteChatId.Should().Be(chatId);
    }

    [Fact]
    public async Task ChatWithRequestsOffShouldOfferInviteLinksOnly()
    {
        // arrange
        await using var owner = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueBob();
        var (chatId, _) = await owner.CreateChat(c => c with {
            IsPublic = false,
            JoinRequestAccess = JoinRequestAccess.Off,
        });
        var invites = owner.AppServices.GetRequiredService<IInvites>();
        var oldInvites = await invites.ListChatInvites(owner.Session, chatId, CancellationToken.None);
        var hub = owner.ScopedAppServices.AppUIHub();

        // act
        var model = await hub.ShareUI.GetModel(chatId, CancellationToken.None);

        // assert
        model.Should().NotBeNull();
        model!.Request.HasLink().Should().BeFalse();
        model.InviteChatId.Should().Be(chatId);
        var newInvites = await invites.ListChatInvites(owner.Session, chatId, CancellationToken.None);
        newInvites.Should().HaveSameCount(oldInvites, "opening Share must not generate an invite");
    }

    // Private methods

    private static async Task SetPlaceAccess(BlazorTester tester, Place place, JoinRequestAccess access)
    {
        var changeCmd = new Places_Change {
            Session = tester.Session,
            PlaceId = place.Id,
            ExpectedVersion = null,
            Change = Change.Update(new PlaceDiff { JoinRequestAccess = access }),
        };
        await tester.Commander.Call(changeCmd);
    }
}
