using System.Security;
using ActualChat.Contacts;
using ActualChat.Invite;
using ActualChat.Queues;
using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(PlaceCollection))]
public class PlaceOperationsTest(PlaceCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private const string PlaceTitle = "AC Place";
    private const string ChatTitle = "General";

    [Fact]
    public async Task TryGetNonExistingPlace()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        await tester.SignInAsBob();

        var services = tester.AppServices;
        var places = services.GetRequiredService<IPlaces>();
        var place = await places.Get(session, new PlaceId("UnknownPlaceId"), default);
        place.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateNewPlace(bool isPublicPlace)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        await tester.SignInAsBob();

        var services = tester.AppServices;
        var places = services.GetRequiredService<IPlaces>();
        var commander = tester.Commander;

        var place = await CreatePlace(commander, session, isPublicPlace);
        place.Should().NotBeNull();

        await services.Queues().WhenProcessing();

        await TestExt.WhenMetAsync(
            async () => {
                place = await places.Get(session, place.Id, default);
                place.Should().NotBeNull();
            },
            TimeSpan.FromSeconds(1));

        place.Title.Should().Be(PlaceTitle);
        place.IsPublic.Should().Be(isPublicPlace);

        var contacts = services.GetRequiredService<IContacts>();
        await TestExt.WhenMetAsync(
            async () => {
                var placeIds = await contacts.ListPlaceIds(session, default);
                placeIds.Count.Should().Be(1);
                placeIds.Should().Contain(place.Id);
            },
            TimeSpan.FromSeconds(3));

        await using var tester2 = appHost.NewBlazorTester();
        var anotherSession = tester2.Session;
        await tester2.SignInAsAlice();

        await services.Queues().WhenProcessing();

        await TestExt.WhenMetAsync(
            async () => {
                var place2 = await places.Get(anotherSession, place.Id, default);
                if (isPublicPlace)
                    place2.Should().NotBeNull();
                else
                    place2.Should().BeNull();
            },
            TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CreatePlaceChat(bool isPublicPlace, bool isPublicChat)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        await tester.SignInAsBob();

        var services = tester.AppServices;
        var chats = services.GetRequiredService<IChats>();
        var commander = tester.Commander;

        var place = await CreatePlace(commander, session, isPublicPlace);

        var chat = await CreateChat(commander, session, place.Id, isPublicChat);
        chat.Should().NotBeNull();

        await services.Queues().WhenProcessing();

        await TestExt.WhenMetAsync(
            async () => {
                chat = await chats.Get(session, chat.Id, default);
                chat.Should().NotBeNull();
            },
            TimeSpan.FromSeconds(1));

        chat.Title.Should().Be(ChatTitle);
        chat.IsPublic.Should().Be(isPublicChat);
        chat.Kind.Should().Be(ChatKind.Place);
        chat.Id.PlaceId.Should().Be(place.Id);

        var contacts = services.GetRequiredService<IContacts>();
        await Task.Delay(100); // Let's wait events are processed
        await TestExt.WhenMetAsync(
            async () => {
                var contactIds = await contacts.ListIds(session, place.Id, default);
                var chatIds = (await contactIds.Select(id => contacts.Get(session, id, default))
                        .Collect())
                    .SkipNullItems()
                    .Select(c => c.ChatId)
                    .ToArray();
                chatIds.Length.Should().Be(1);
                chatIds.Should().Contain(chat.Id);
            },
            TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WelcomeChatShouldBeAccessible(bool isPublicPlace)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        await tester.SignInAsBob();

        var services = tester.AppServices;
        var places = services.GetRequiredService<IPlaces>();
        var commander = tester.Commander;

        var place = await CreatePlace(commander, session, isPublicPlace);

        var welcomeChat = await CreateChat(commander, session, place.Id, true, "Welcome");
        {
            var welcomeChatId = await places.GetWelcomeChatId(session, place.Id, default);
            welcomeChatId.Should().Be(welcomeChat.Id);
        }

        await using var tester2 = appHost.NewBlazorTester();
        var anotherSession = tester2.Session;
        var commander2 = tester2.Commander;
        await tester2.SignInAsAlice();

        {
            var welcomeChatId = await places.GetWelcomeChatId(anotherSession, place.Id, default);
            welcomeChatId.Should().Be(isPublicPlace ? welcomeChat.Id : ChatId.None);
        }

        if (!isPublicPlace) {
            var invite = ActualChat.Invite.Invite.New(Constants.Invites.Defaults.PlaceRemaining, new PlaceInviteOption(place.Id));
            invite = await commander.Call(new Invites_Generate(session, invite));

            await commander2.Call(new Invites_Use(anotherSession, invite.Id));
        }

        await TestExt.WhenMetAsync(
            async () => {
                var welcomeChatId = await places.GetWelcomeChatId(anotherSession, place.Id, default);
                welcomeChatId.Should().Be(welcomeChat.Id);
            },
            TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JoinPlace(bool isPublicPlace)
    {
        using var appHost = await NewAppHost(nameof(JoinPlace));
        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        await tester.SignInAsBob();

        var services = tester.AppServices;
        var commander = tester.Commander;

        var place = await CreatePlace(commander, session, isPublicPlace);

        await using var tester2 = appHost.NewBlazorTester();
        var anotherSession = tester2.Session;
        await tester2.SignInAsAlice();
        var contacts = services.GetRequiredService<IContacts>();

        {
            var placeIds = await contacts.ListPlaceIds(anotherSession, default);
            placeIds.Should().BeEmpty();
        }

        if (!isPublicPlace) {
            var invite = ActualChat.Invite.Invite.New(Constants.Invites.Defaults.PlaceRemaining, new PlaceInviteOption(place.Id));
            invite = await commander.Call(new Invites_Generate(session, invite));

            await tester2.Commander.Call(new Invites_Use(anotherSession, invite.Id));
        }

        await commander.Call(new Places_Join(anotherSession, place.Id));
        await services.Queues().WhenProcessing();

        await TestExt.WhenMetAsync(
            async () => {
                var placeIds = await contacts.ListPlaceIds(anotherSession, default);
                placeIds.Count.Should().Be(1);
                placeIds.Should().Contain(place.Id);
            },
            TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeavePlace(bool isPublicPlace)
    {
        using var appHost = await NewAppHost(nameof(LeavePlace));
        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        await tester.SignInAsBob();
        var commander = tester.Commander;

        var place = await CreatePlace(commander, session, isPublicPlace);
        var placeId = place.Id;

        await using var tester2 = appHost.NewBlazorTester();
        var anotherSession = tester2.Session;
        await tester2.SignInAsAlice();
        var services = tester2.AppServices;
        var contacts = services.GetRequiredService<IContacts>();
        var places = services.GetRequiredService<IPlaces>();
        {
            var placeIds = await contacts.ListPlaceIds(anotherSession, default);
            placeIds.Should().BeEmpty();
        }

        var inviteId = Symbol.Empty;
        if (!isPublicPlace) {
            var invite = ActualChat.Invite.Invite.New(Constants.Invites.Defaults.PlaceRemaining, new PlaceInviteOption(placeId));
            invite = await commander.Call(new Invites_Generate(session, invite));
            inviteId = invite.Id;

            await tester2.Commander.Call(new Invites_Use(anotherSession, inviteId));
        }

        await commander.Call(new Places_Join(anotherSession, placeId));

        await TestExt.WhenMetAsync(
            async () => {
                var placeIds = await contacts.ListPlaceIds(anotherSession, default);
                placeIds.Should().HaveCount(1);
                placeIds.Should().Contain(placeId);
            },
            TimeSpan.FromSeconds(3));

        place = await places.Get(anotherSession, placeId, default);
        place.Should().NotBeNull();
        place!.Rules.CanLeave().Should().BeTrue();

        // Leave
        await commander.Call(new Places_Leave(anotherSession, placeId));
        await services.Queues().WhenProcessing();

        await ComputedTestExt.When(
            services,
            async ct => {
                var placeIds = await contacts.ListPlaceIds(anotherSession, ct);
                placeIds.Should().BeEmpty();
            },
            TimeSpan.FromSeconds(5));

        place = await places.Get(anotherSession, placeId, default);
        if (isPublicPlace)
            place.Should().NotBeNull();
        else
            place.Should().BeNull();

        // Re-join again
        if (!isPublicPlace)
            await tester2.Commander.Call(new Invites_Use(anotherSession, inviteId));
        await commander.Call(new Places_Join(anotherSession, placeId));

        await TestExt.WhenMetAsync(
            async () => {
                var placeIds = await contacts.ListPlaceIds(anotherSession, default);
                placeIds.Should().HaveCount(1);
                placeIds.Should().Contain(placeId);
            },
            TimeSpan.FromSeconds(3));

        place = await places.Get(anotherSession, placeId, default);
        place.Should().NotBeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task JoinPlaceChat(bool isPublicPlace, bool isPublicChat)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        await tester.SignInAsBob();

        var commander = tester.Commander;

        var (place, chat) = await CreatePlaceWithDefaultChat(commander, session, isPublicPlace, isPublicChat);

        await using var tester2 = appHost.NewBlazorTester();
        var anotherSession = tester2.Session;
        var commander2 = tester2.Commander;
        await tester2.SignInAsAlice();
        var contacts = tester2.AppServices.GetRequiredService<IContacts>();
        {
            var placeIds = await contacts.ListPlaceIds(anotherSession, default);
            placeIds.Should().BeEmpty();
        }

        if (!isPublicPlace) {
            var invite = ActualChat.Invite.Invite.New(Constants.Invites.Defaults.PlaceRemaining, new PlaceInviteOption(place.Id));
            invite = await commander.Call(new Invites_Generate(session, invite));

            await commander2.Call(new Invites_Use(anotherSession, invite.Id));
        }
        await appHost.Services.Queues().WhenProcessing();

        if (isPublicChat) {
            // Assert user can see the Chat while previewing the Place.
            await TestExt.WhenMetAsync(
                async () => {
                    var contactIds = await contacts.ListIds(anotherSession, place.Id, default);
                    var chatIds = (await contactIds.Select(id => contacts.Get(anotherSession, id, default))
                            .Collect())
                        .SkipNullItems()
                        .Select(c => c.ChatId)
                        .ToArray();
                    chatIds.Length.Should().Be(1);
                    chatIds.Should().Contain(chat.Id);
                },
                TimeSpan.FromSeconds(3));
        }
        await commander2.Call(new Places_Join(anotherSession, place.Id));
        await appHost.Services.Queues().WhenProcessing();

        // Assert user can see the Place.
        await TestExt.WhenMetAsync(
            async () => {
                var placeIds = await contacts.ListPlaceIds(anotherSession, default);
                placeIds.Count.Should().Be(1);
                placeIds.Should().Contain(place.Id);
            },
            TimeSpan.FromSeconds(3));

        if (!isPublicChat) {
            var contactIds = await contacts.ListIds(anotherSession, place.Id, default);
            contactIds.Count.Should().Be(0);

            var invite = ActualChat.Invite.Invite.New(Constants.Invites.Defaults.ChatRemaining, new ChatInviteOption(chat.Id));
            invite = await commander.Call(new Invites_Generate(session, invite));

            await commander2.Call(new Invites_Use(anotherSession, invite.Id));
            await commander2.Call(new Authors_Join(anotherSession, chat.Id));
        }
        await appHost.Services.Queues().WhenProcessing();

        // Assert user can see the Chat.
        await TestExt.WhenMetAsync(
            async () => {
                var contactIds = await contacts.ListIds(anotherSession, place.Id, default);
                var chatIds = (await contactIds.Select(id => contacts.Get(anotherSession, id, default))
                        .Collect())
                    .SkipNullItems()
                    .Select(c => c.ChatId)
                    .ToArray();
                chatIds.Length.Should().Be(1);
                chatIds.Should().Contain(chat.Id);
            },
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task ItShouldBeNotPossibleToActivateInviteLinkToChatOnNonAccessiblePrivatePlace()
    {
        using var appHost = await NewAppHost("private-place");
        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        await tester.SignInAsBob();

        var commander = tester.Commander;

        var (place, chat) = await CreatePlaceWithDefaultChat(commander, session, false, false);

        await using var tester2 = appHost.NewBlazorTester();
        var anotherSession = tester2.Session;
        var commander2 = tester2.Commander;
        await tester2.SignInAsAlice();
        var contacts = tester2.AppServices.GetRequiredService<IContacts>();
        {
            var placeIds = await contacts.ListPlaceIds(anotherSession, default);
            placeIds.Should().BeEmpty();
        }

        var contactIds = await contacts.ListIds(anotherSession, place.Id, default);
        contactIds.Count.Should().Be(0);

        var invite = ActualChat.Invite.Invite.New(Constants.Invites.Defaults.ChatRemaining, new ChatInviteOption(chat.Id));
        invite = await commander.Call(new Invites_Generate(session, invite));

        await Assert.ThrowsAsync<SecurityException>(async () => {
            await commander2.Call(new Invites_Use(anotherSession, invite.Id));
        });
    }

    [Fact]
    public async Task PlaceChatMembership()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        var session1 = tester.Session;
        await tester.SignInAsBob();

        var services = tester.AppServices;
        var authors = services.GetRequiredService<IAuthors>();

        var (place, chat) = await CreatePlaceWithDefaultChat(tester.Commander, session1);

        await using var tester2 = appHost.NewBlazorTester();
        var session2 = tester2.Session;
        await tester2.SignInAsAlice();

        await tester2.Commander.Call(new Places_Join(session2, place.Id));

        var authorList1 = await authors.ListAuthorIds(session1, chat.Id, default);
        authorList1.Should().HaveCount(2);
        var authorList2 = await authors.ListAuthorIds(session2, chat.Id, default);
        authorList2.Should().HaveCount(2);
        authorList1.Should().BeEquivalentTo(authorList2);

        foreach (var authorId in authorList1)
            authorId.ChatId.Should().Be(chat.Id);

        var ownAuthor1 = await authors.GetOwn(session1, chat.Id, default);
        ownAuthor1.Should().NotBeNull();
        ownAuthor1!.ChatId.Should().Be(chat.Id);

        var ownAuthor2 = await authors.GetOwn(session2, chat.Id, default);
        ownAuthor2.Should().NotBeNull();
        ownAuthor2!.ChatId.Should().Be(chat.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ItShouldBeNotPossibleToLeavePublicChatOnPlace(bool isPublicPlace)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        await tester.SignInAsBob();

        var services = tester.AppServices;
        var chats = services.GetRequiredService<IChats>();
        var commander = tester.Commander;

        var (_, chat) = await CreatePlaceWithDefaultChat(commander, session, isPublicPlace: isPublicPlace);

        await TestExt.WhenMetAsync(
            async () => {
                chat = await chats.Get(session, chat.Id, default);
                chat.Should().NotBeNull();
            },
            TimeSpan.FromSeconds(1));

        chat.IsPublic.Should().BeTrue();
        chat.Rules.CanLeave().Should().BeFalse();

        await Assert.ThrowsAsync<SecurityException>(() =>
            commander.Call(new Authors_Leave(session, chat.Id)
        ));
    }

    [Fact]
    public async Task UpsertTextEntry()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        var session1 = tester.Session;
        await tester.SignInAsBob();

        var commander1 = tester.Commander;

        var (place, chat) = await CreatePlaceWithDefaultChat(commander1, session1);

        await using var tester2 = appHost.NewBlazorTester();
        var session2 = tester2.Session;
        await tester2.SignInAsAlice();
        var commander2 = tester2.Commander;

        await commander2.Call(new Places_Join(session2, place.Id));

        var chatEntry1 = await commander1.Call(new Chats_UpsertTextEntry(session1, chat.Id, null, "My first message"));
        chatEntry1.Should().NotBeNull();

        var chatEntry2 = await commander2.Call(new Chats_UpsertTextEntry(session2, chat.Id, null, "And mine first message"));
        chatEntry2.Should().NotBeNull();
    }

    [Fact]
    public async Task UpsertTextEntryToPublicPlaceChatShouldEnsureThatExplicitAuthorExist()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        var session1 = tester.Session;
        await tester.SignInAsBob();

        var commander1 = tester.Commander;

        var (place, chat) = await CreatePlaceWithDefaultChat(commander1, session1);

        await using var tester2 = appHost.NewBlazorTester();
        var session2 = tester2.Session;
        await tester2.SignInAsAlice();

        var account = await tester2.AppServices.GetRequiredService<IAccounts>().GetOwn(session2, default);
        await commander1.Call(new Places_Invite(session1, place.Id, new [] { account.Id }));

        var commander2 = tester2.Commander;
        var chatEntry1 = await commander2.Call(new Chats_UpsertTextEntry(session2, chat.Id, null, "My first message"));
        chatEntry1.Should().NotBeNull();
        var authorId = chatEntry1.AuthorId;

        var authorsBackend = tester2.AppServices.GetRequiredService<IAuthorsBackend>();
        var explicitAuthor = await authorsBackend.Get(authorId.ChatId, authorId, AuthorsBackend_GetAuthorOption.Raw, default);
        explicitAuthor.Should().NotBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NonPlaceMembersShouldBeAbleToReadPublicPlacesOnly(bool isPublicPlace)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        var session1 = tester.Session;
        await tester.SignInAsBob();
        var commander1 = tester.Commander;

        var place = await CreatePlace(commander1, session1, isPublicPlace);

        await using var tester2 = appHost.NewBlazorTester();
        var session2 = tester2.Session;
        await tester2.SignInAsAlice();
        var places = tester2.AppServices.GetRequiredService<IPlaces>();

        var place1 = await places.Get(session2, place.Id, default);
        if (isPublicPlace)
            place1.Should().NotBeNull();
        else
            place1.Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NonPlaceMembersShouldBeNotAbleToAddChat(bool isPublicChat)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        var session1 = tester.Session;
        await tester.SignInAsBob();

        var commander1 = tester.Commander;

        var place = await CreatePlace(commander1, session1, true);

        await using var tester2 = appHost.NewBlazorTester();
        var session2 = tester2.Session;
        await tester2.SignInAsAlice();
        var commander2 = tester2.Commander;

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateChat(
            commander2,
            session2,
            place.Id,
            isPublicChat));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ItShouldBeNotPossibleToAddChatToPlaceYouHaveNoAccessTo(bool isPublicChat)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        var session1 = tester.Session;
        await tester.SignInAsBob();

        var commander1 = tester.Commander;

        var place = await CreatePlace(commander1, session1, false);

        await using var tester2 = appHost.NewBlazorTester();
        var session2 = tester2.Session;
        await tester2.SignInAsAlice();
        var commander2 = tester2.Commander;
        var places = tester2.AppServices.GetRequiredService<IPlaces>();
        var place1 = await places.Get(session2, place.Id, default);
        place1.Should().BeNull();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateChat(
            commander2,
            session2,
            place.Id,
            isPublicChat));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task OnlyPlaceOwnerShouldBeAbleToCreatePublicChats(bool isOwner, bool isPublicChat, bool shouldSucceed)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        var session1 = tester.Session;
        await tester.SignInAsBob();

        var commander1 = tester.Commander;

        var place = await CreatePlace(commander1, session1, true);

        await using var tester2 = appHost.NewBlazorTester();
        var session2 = tester2.Session;
        await tester2.SignInAsAlice();
        var commander2 = tester2.Commander;

        await commander2.Call(new Places_Join(session2, place.Id));

        if (shouldSucceed)
            (await AddChat()).Should().NotBeNull();
        else
            await Assert.ThrowsAsync<SecurityException>(AddChat);

        Task<Chat> AddChat()
        {
            var (session, commander) = isOwner ? (session1, commander1) : (session2, commander2);
            return CreateChat(commander, session, place.Id, isPublicChat);
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task OnlyPlaceOwnerShouldBeAbleToSwitchChatFromPrivateToPublic(bool isOwner, bool shouldSucceed)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        var session1 = tester.Session;
        await tester.SignInAsBob();

        var commander1 = tester.Commander;

        var place = await CreatePlace(commander1, session1, true);
        var (session, commander) = (session1, commander1);

        if (!isOwner) {
            await using var tester2 = appHost.NewBlazorTester();
            var session2 = tester2.Session;
            await tester2.SignInAsAlice();
            var commander2 = tester2.Commander;

            await commander2.Call(new Places_Join(session2, place.Id));
            (session, commander) = (session2, commander2);
        }

        var chat = await CreateChat(commander, session, place.Id, false);

        if (shouldSucceed)
            (await MakeChatPublic()).Should().NotBeNull();
        else
            await Assert.ThrowsAsync<SecurityException>(MakeChatPublic);
        return;

        Task<Chat> MakeChatPublic()
        {
            return commander.Call(new Chats_Change(session, chat.Id, null, new () {
                Update = new ChatDiff {
                    IsPublic = true,
                },
            }));
        }
    }

    private static async Task<(Place, Chat)> CreatePlaceWithDefaultChat(
        ICommander commander,
        Session session,
        bool isPublicPlace = true,
        bool isPublicChat = true)
    {
        var place = await CreatePlace(commander, session, isPublicPlace);
        var chat = await CreateChat(commander, session, place.Id, isPublicChat);
        return (place, chat);
    }

    private static async Task<Place> CreatePlace(
        ICommander commander,
        Session session,
        bool isPublicPlace)
    {
        var place = await commander.Call(new Places_Change(session, default, null, new () {
            Create = new PlaceDiff {
                Title = PlaceTitle,
                IsPublic = isPublicPlace,
            },
        }));
        return place;
    }

    private static async Task<Chat> CreateChat(
        ICommander commander,
        Session session,
        PlaceId placeId,
        bool isPublicChat,
        string chatTitle = ChatTitle)
    {
        var chat = await commander.Call(new Chats_Change(session, default, null, new () {
            Create = new ChatDiff {
                Title = chatTitle,
                IsPublic = isPublicChat,
                PlaceId = placeId,
            },
        }));
        return chat;
    }
}
