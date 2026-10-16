using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class JoinRequestAccessTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
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
    public async Task NewPrivateChatShouldDefaultToSignedIn()
    {
        // arrange
        await Owner.SignInAsUniqueBob();

        // act
        var (chat, _) = await Owner.CreateAndGetChat(false);
        var (publicChat, _) = await Owner.CreateAndGetChat(true);

        // assert
        chat.JoinRequestAccess.Should().Be(Constants.Chat.DefaultJoinRequestAccess);
        publicChat.JoinRequestAccess.Should().Be(JoinRequestAccess.Off, "a public chat needs no requests");
    }

    [Fact]
    public async Task PlaceValueShouldReachItsRootChat()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        var place = await Owner.CreatePlace(false, "access place");
        var changeCmd = new Places_Change {
            Session = Owner.Session,
            PlaceId = place.Id,
            ExpectedVersion = place.Version,
            Change = Change.Update(new PlaceDiff { JoinRequestAccess = JoinRequestAccess.Anyone }),
        };

        // act
        await Owner.Commander.Call(changeCmd);

        // assert
        var chatsBackend = Owner.AppServices.GetRequiredService<IChatsBackend>();
        await TestWait.When(async ct => {
            var rootChat = await chatsBackend.Get(place.Id.RootChatId, ct).Require();
            rootChat.JoinRequestAccess.Should().Be(JoinRequestAccess.Anyone);
        });
        place.JoinRequestAccess.Should().Be(Constants.Chat.DefaultJoinRequestAccess);
    }

    [Fact]
    public async Task PlaceMembersShouldBeRefusedOutsidePlaceChats()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        var place = await Owner.CreatePlace(false, "access place");
        var (chatId, _) = await Owner.CreateChat(false);
        var (placeChatId, _) = await Owner.CreateChat(false, "place chat", place.Id);
        var changePlaceCmd = new Places_Change {
            Session = Owner.Session,
            PlaceId = place.Id,
            ExpectedVersion = null,
            Change = Change.Update(new PlaceDiff { JoinRequestAccess = JoinRequestAccess.PlaceMembers }),
        };

        // act
        var changeChat = () => ChangeChat(Owner, chatId, JoinRequestAccess.PlaceMembers);
        var changePlace = () => Owner.Commander.Call(changePlaceCmd);
        var placeChat = await ChangeChat(Owner, placeChatId, JoinRequestAccess.PlaceMembers);

        // assert
        await changeChat.Should().ThrowAsync<Exception>("a standalone chat has no Place members");
        await changePlace.Should().ThrowAsync<Exception>("a Place can't be limited to its own members");
        placeChat.JoinRequestAccess.Should().Be(JoinRequestAccess.PlaceMembers);
    }

    [Fact]
    public async Task ModeratorShouldChangeItButPlainMemberShouldNot()
    {
        // arrange
        await Owner.SignInAsUniqueBob();
        var moderatorAccount = await Moderator.SignInAsUniqueAlice();
        var memberAccount = await Member.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(false);
        var moderatorAuthor = await Owner.InviteToChat(chatId, moderatorAccount.Id);
        await Owner.InviteToChat(chatId, memberAccount.Id);
        var makeModeratorCmd = new Authors_ChangeRole {
            Session = Owner.Session,
            AuthorId = moderatorAuthor.Id,
            SystemRole = SystemRole.Moderator,
            IsInRole = true,
        };
        await Owner.Commander.Call(makeModeratorCmd);

        // act
        var chat = await ChangeChat(Moderator, chatId, JoinRequestAccess.Off);
        var changeByMember = () => ChangeChat(Member, chatId, JoinRequestAccess.Anyone);

        // assert
        chat.JoinRequestAccess.Should().Be(JoinRequestAccess.Off);
        await changeByMember.Should().ThrowAsync<Exception>("changing it takes EditProperties");
    }

    // Private methods

    private static Task<Chat> ChangeChat(WebClientTester tester, ChatId chatId, JoinRequestAccess access)
    {
        var changeCmd = new Chats_Change {
            Session = tester.Session,
            ChatId = chatId,
            ExpectedVersion = null,
            Change = Change.Update(new ChatDiff { JoinRequestAccess = access }),
        };
        return tester.Commander.Call(changeCmd);
    }
}
