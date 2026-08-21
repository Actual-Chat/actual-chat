using System.Security;
using ActualChat.Contacts;
using ActualChat.Invite;
using ActualChat.Queues;
using ActualChat.Testing.Host;

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
        await using var tester = AppHost.NewBlazorTester(Out);
        var session = tester.Session;
        await tester.SignInAsUniqueBob();

        var services = tester.AppServices;
        var places = services.GetRequiredService<IPlaces>();
        var place = await places.Get(session, PlaceId.Parse("UnknownPlaceId"), default);
        place.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateNewPlace(bool isPublicPlace)
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        var session = tester.Session;
        await tester.SignInAsUniqueBob();

        var services = tester.AppServices;
        var places = services.GetRequiredService<IPlaces>();
        var commander = tester.Commander;

        var place = await CreatePlace(commander, session, isPublicPlace);
        place.Should().NotBeNull();

        place = await ComputedTest.When(async ct => {
            place = await places.Get(session, place.Id, ct);
            place.Should().NotBeNull();
            return place!;
        });

        place.Title.Should().Be(PlaceTitle);
        place.IsPublic.Should().Be(isPublicPlace);

        var contacts = services.GetRequiredService<IContacts>();
        await ComputedTest.When(async ct => {
            var placeIds = await contacts.ListPlaceIds(session, ct);
            placeIds.Length.Should().Be(1);
            placeIds.Should().Contain(place.Id);
        }, TimeSpan.FromSeconds(10));

        await using var tester2 = AppHost.NewBlazorTester(Out);
        var anotherSession = tester2.Session;
        await tester2.SignInAsUniqueAlice();

        await ComputedTest.When(async ct => {
            var place2 = await places.Get(anotherSession, place.Id, ct);
            if (isPublicPlace)
                place2.Should().NotBeNull();
            else
                place2.Should().BeNull();
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CreatePlaceChat(bool isPublicPlace, bool isPublicChat)
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        var session = tester.Session;
        await tester.SignInAsUniqueBob();

        var services = tester.AppServices;
        var chats = services.GetRequiredService<IChats>();
        var commander = tester.Commander;

        var place = await CreatePlace(commander, session, isPublicPlace);

        var chat = await CreateChat(commander, session, place.Id, isPublicChat);
        chat.Should().NotBeNull();

        chat = await ComputedTest.When(async ct => {
            chat = await chats.Get(session, chat.Id, ct);
            chat.Should().NotBeNull();
            return chat!;
        });

        chat.Title.Should().Be(ChatTitle);
        chat.IsPublic.Should().Be(isPublicChat);
        chat.Kind.Should().Be(ChatKind.Place);
        ((PlaceChatId)chat.Id).PlaceId.Should().Be(place.Id);

        var contacts = services.GetRequiredService<IContacts>();
        await Task.Delay(100); // Let's wait events are processed
        await ComputedTest.When(async ct => {
            var contactIds = await contacts.ListIds(session, place.Id, ct);
            var chatIds = (await contactIds.Select(id => contacts.Get(session, id, ct))
                .Collect(ct))
                .SkipNullItems()
                .Select(c => c.ChatId)
                .ToArray();
            chatIds.Length.Should().Be(1);
            chatIds.Should().Contain(chat.Id);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WelcomeChatShouldBeAccessible(bool isPublicPlace)
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        var session = tester.Session;
        await tester.SignInAsUniqueBob();

        var services = tester.AppServices;
        var places = services.GetRequiredService<IPlaces>();
        var commander = tester.Commander;

        var place = await CreatePlace(commander, session, isPublicPlace);

        var welcomeChat = await CreateChat(commander, session, place.Id, true, "Welcome");
        {
            var welcomeChatId = await places.GetWelcomeChatId(session, place.Id, default);
            welcomeChatId.Should().Be(welcomeChat.Id);
        }

        await using var tester2 = AppHost.NewBlazorTester(Out);
        var anotherSession = tester2.Session;
        var commander2 = tester2.Commander;
        await tester2.SignInAsUniqueAlice();

        {
            var welcomeChatId = await places.GetWelcomeChatId(anotherSession, place.Id, default);
            welcomeChatId.Should().Be(isPublicPlace ? welcomeChat.Id : null);
        }

        if (!isPublicPlace) {
            ActualChat.Invite.Invite invite = ActualChat.Invite.PlaceInvite.New(Constants.Invites.Defaults.PlaceRemaining, place.Id);
            invite = await commander.Call(new Invites_Generate { Session = session, Invite = invite });

            await commander2.Call(new Invites_Use { Session = anotherSession, InviteId = invite.Id });
        }

        await ComputedTest.When(async ct => {
            var welcomeChatId = await places.GetWelcomeChatId(anotherSession, place.Id, ct);
            welcomeChatId.Should().Be(welcomeChat.Id);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JoinPlace(bool isPublicPlace)
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        var session = tester.Session;
        await tester.SignInAsUniqueBob();

        var services = tester.AppServices;
        var commander = tester.Commander;

        var place = await CreatePlace(commander, session, isPublicPlace);

        await using var tester2 = AppHost.NewBlazorTester(Out);
        var anotherSession = tester2.Session;
        await tester2.SignInAsUniqueAlice();
        var contacts = services.GetRequiredService<IContacts>();

        {
            var placeIds = await contacts.ListPlaceIds(anotherSession, default);
            placeIds.Should().BeEmpty();
        }

        if (!isPublicPlace) {
            ActualChat.Invite.Invite invite = ActualChat.Invite.PlaceInvite.New(Constants.Invites.Defaults.PlaceRemaining, place.Id);
            invite = await commander.Call(new Invites_Generate { Session = session, Invite = invite });

            await tester2.Commander.Call(new Invites_Use { Session = anotherSession, InviteId = invite.Id });
        }

        await commander.Call(new Places_Join { Session = anotherSession, PlaceId = place.Id });

        await ComputedTest.When(async ct => {
            var placeIds = await contacts.ListPlaceIds(anotherSession, ct);
            placeIds.Should().BeEquivalentTo([place.Id]);
        }, TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeavePlace(bool isPublicPlace)
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        var session = tester.Session;
        await tester.SignInAsUniqueBob();
        var commander = tester.Commander;

        var place = await CreatePlace(commander, session, isPublicPlace);
        var placeId = place.Id;

        await using var tester2 = AppHost.NewBlazorTester(Out);
        var anotherSession = tester2.Session;
        await tester2.SignInAsUniqueAlice();
        var services = tester2.AppServices;
        var contacts = services.GetRequiredService<IContacts>();
        var places = services.GetRequiredService<IPlaces>();
        {
            var placeIds = await contacts.ListPlaceIds(anotherSession, default);
            placeIds.Should().BeEmpty();
        }

        var inviteId = Symbol.Empty;
        if (!isPublicPlace) {
            ActualChat.Invite.Invite invite = ActualChat.Invite.PlaceInvite.New(Constants.Invites.Defaults.PlaceRemaining, placeId);
            invite = await commander.Call(new Invites_Generate { Session = session, Invite = invite });
            inviteId = invite.Id;

            await tester2.Commander.Call(new Invites_Use { Session = anotherSession, InviteId = inviteId });
        }

        await commander.Call(new Places_Join { Session = anotherSession, PlaceId = placeId });

        await ComputedTest.When(async ct => {
                var placeIds = await contacts.ListPlaceIds(anotherSession, ct);
                placeIds.Should().BeEquivalentTo([placeId]);
            },
            TimeSpan.FromSeconds(10));

        place = await places.Get(anotherSession, placeId, default);
        place.Should().NotBeNull();
        place!.Rules.CanLeave().Should().BeTrue();

        // Leave
        await commander.Call(new Places_Leave { Session = anotherSession, PlaceId = placeId });

        await ComputedTest.When(async ct => {
            var placeIds = await contacts.ListPlaceIds(anotherSession, ct);
            placeIds.Should().BeEmpty();
        });

        await ComputedTest.When(async ct => {
            place = await places.Get(anotherSession, placeId, ct);
            if (isPublicPlace)
                place.Should().NotBeNull();
            else
                place.Should().BeNull();
        });

        // Re-join again
        if (!isPublicPlace) {
            await tester2.Commander.Call(new Invites_Use { Session = anotherSession, InviteId = inviteId });
            await ComputedTest.When(async ct => {
                var rejoinable = await places.Get(anotherSession, placeId, ct);
                rejoinable!.Rules.CanJoin().Should().BeTrue();
            });
        }
        await commander.Call(new Places_Join { Session = anotherSession, PlaceId = placeId });

        await ComputedTest.When(async ct => {
            var placeIds = await contacts.ListPlaceIds(anotherSession, ct);
            placeIds.Should().BeEquivalentTo([placeId]);

            place = await places.Get(anotherSession, placeId, default);
            place.Should().NotBeNull();
        }, TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task JoinPlaceChat(bool isPublicPlace, bool isPublicChat)
    {
        var services = AppHost.Services;
        await using var tester = AppHost.NewBlazorTester(Out);
        var session = tester.Session;
        await tester.SignInAsUniqueBob();

        var commander = tester.Commander;

        var (place, chat) = await CreatePlaceWithDefaultChat(commander, session, isPublicPlace, isPublicChat);

        await using var tester2 = AppHost.NewBlazorTester(Out);
        var anotherSession = tester2.Session;
        var commander2 = tester2.Commander;
        await tester2.SignInAsUniqueAlice();
        var contacts = tester2.AppServices.GetRequiredService<IContacts>();
        {
            var placeIds = await contacts.ListPlaceIds(anotherSession, default);
            placeIds.Should().BeEmpty();
        }

        if (!isPublicPlace) {
            ActualChat.Invite.Invite invite = ActualChat.Invite.PlaceInvite.New(Constants.Invites.Defaults.PlaceRemaining, place.Id);
            invite = await commander.Call(new Invites_Generate { Session = session, Invite = invite });

            await commander2.Call(new Invites_Use { Session = anotherSession, InviteId = invite.Id });
        }

        if (isPublicChat) {
            // Assert user can see the Chat while previewing the Place.
            await ComputedTest.When(async ct => {
                var contactIds = await contacts.ListIds(anotherSession, place.Id, ct);
                var chatIds = (await contactIds.Select(id => contacts.Get(anotherSession, id, ct))
                    .Collect(ct))
                    .SkipNullItems()
                    .Select(c => c.ChatId)
                    .ToArray();
                chatIds.Length.Should().Be(1);
                chatIds.Should().Contain(chat.Id);
            });
        }
        else
            await services.Queues().WhenProcessing();

        await commander2.Call(new Places_Join { Session = anotherSession, PlaceId = place.Id });

        // Assert user can see the Place.
        await ComputedTest.When(async ct => {
            var placeIds = await contacts.ListPlaceIds(anotherSession, ct);
            placeIds.Length.Should().Be(1);
            placeIds.Should().Contain(place.Id);
        }, TimeSpan.FromSeconds(10));

        if (!isPublicChat) {
            var contactIds = await contacts.ListIds(anotherSession, place.Id, default);
            contactIds.Length.Should().Be(0);

            ActualChat.Invite.Invite invite = ActualChat.Invite.ChatInvite.New(Constants.Invites.Defaults.ChatRemaining, chat.Id);
            invite = await commander.Call(new Invites_Generate { Session = session, Invite = invite });

            // Invites_Use requires place membership, which the just-issued Places_Join grants a
            // recompute later - ChatsBackend.GetRules consolidates.
            var chatsBackend = AppHost.Services.GetRequiredService<IChatsBackend>();
            var joiner = await tester2.Accounts.GetOwn(anotherSession, default);
            await ComputedTest.When(async ct => {
                var placeRules = await chatsBackend.GetRules(place.Id.RootChatId, joiner.Id, ct);
                placeRules.IsMember().Should().BeTrue();
            });
            await commander2.Call(new Invites_Use { Session = anotherSession, InviteId = invite.Id });
            await commander2.Call(new Authors_Join { Session = anotherSession, ChatId = chat.Id });
        }

        // Assert user can see the Chat.
        await ComputedTest.When(async ct => {
            var contactIds = await contacts.ListIds(anotherSession, place.Id, ct);
            var chatIds = (await contactIds.Select(id => contacts.Get(anotherSession, id, ct))
                .Collect(ct))
                .SkipNullItems()
                .Select(c => c.ChatId)
                .ToArray();
            chatIds.Length.Should().Be(1);
            chatIds.Should().Contain(chat.Id);
        });
    }

    [Theory]
    [InlineData(false, false, false)] // NotPossibleToActivateInviteLinkToPrivateChatOnPrivatePlaceIfYouAreNotMemberOfThePlace
    [InlineData(true, false, true)] // PossibleToActivateInviteLinkToPublicChatOnPrivatePlaceEvenIfYouAreNotMemberOfThePlace
    [InlineData(false, true, true)] // PossibleToActivateInviteLinkToPrivateChatOnPrivatePlaceIfYouAreMemberOfThePlace
    public async Task ActivateInviteLinkToPrivatePlaceChat(bool isPublicChat, bool addToPlaceMembers, bool shouldSucceed)
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        var session = tester.Session;
        await tester.SignInAsUniqueBob();

        var commander = tester.Commander;

        var (place, chat) = await CreatePlaceWithDefaultChat(commander, session, false, isPublicChat);

        await using var tester2 = AppHost.NewBlazorTester(Out);
        var session2 = tester2.Session;
        var commander2 = tester2.Commander;
        await tester2.SignInAsUniqueAlice();
        var contacts = tester2.AppServices.GetRequiredService<IContacts>();
        {
            var placeFromUser2Perspective = await tester2.Places.Get(session2, place.Id, default);
            placeFromUser2Perspective.Should().BeNull();
            var placeIds = await contacts.ListPlaceIds(session2, default);
            placeIds.Should().BeEmpty();
        }

        var contactIds = await contacts.ListIds(session2, place.Id, default);
        contactIds.Length.Should().Be(0);

        if (addToPlaceMembers) {
            var user2 = await tester2.Accounts.GetOwn(session2, default);
            await commander.Call(new Places_Invite { Session = session, PlaceId = place.Id, UserIds = [user2.Id] });
            var chatsBackend = AppHost.Services.GetRequiredService<IChatsBackend>();
            await ComputedTest.When(async ct => {
                var placeFromUser2Perspective = await tester2.Places.Get(session2, place.Id, ct);
                placeFromUser2Perspective.Should().NotBeNull();
                var placeRules = await chatsBackend.GetRules(place.Id.RootChatId, user2.Id, ct);
                placeRules.IsMember().Should().BeTrue();
            });
        }

        ActualChat.Invite.Invite invite = ActualChat.Invite.ChatInvite.New(Constants.Invites.Defaults.ChatRemaining, chat.Id);
        invite = await commander.Call(new Invites_Generate { Session = session, Invite = invite });

        if (shouldSucceed) {
            var invite2 = await commander2.Call(new Invites_Use { Session = session2, InviteId = invite.Id });
            invite2.Should().NotBeNull();
        }
        else
            await Assert.ThrowsAsync<InvalidOperationException>(async () => {
                await commander2.Call(new Invites_Use { Session = session2, InviteId = invite.Id });
            });
    }

    [Fact]
    public async Task PlaceChatMembership()
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        var session1 = tester.Session;
        await tester.SignInAsUniqueBob();

        var services = tester.AppServices;
        var authors = services.GetRequiredService<IAuthors>();

        var (place, chat) = await CreatePlaceWithDefaultChat(tester.Commander, session1);

        await using var tester2 = AppHost.NewBlazorTester(Out);
        var session2 = tester2.Session;
        await tester2.SignInAsUniqueAlice();

        await tester2.Commander.Call(new Places_Join { Session = session2, PlaceId = place.Id });

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

    [Fact]
    public async Task ShouldNotLeavePlaceAfterLeavingPrivatePlaceChat()
    {
        // arrange
        var services = AppHost.Services;
        var authors = services.GetRequiredService<IAuthors>();
        var contacts = services.GetRequiredService<IContacts>();
        var roles = services.GetRequiredService<IRoles>();

        await using var tester1 = AppHost.NewBlazorTester(Out);
        var session1 = tester1.Session;
        await tester1.SignInAsUniqueBob();

        var (place, chat) = await CreatePlaceWithDefaultChat(tester1.Commander, session1, true, false);
        var chatAuthor1 = await authors.GetOwn(session1, chat.Id, CancellationToken.None).Require();

        await using var tester2 = AppHost.NewBlazorTester(Out);
        var session2 = tester2.Session;
        var account2 = await tester2.SignInAsUniqueAlice();
        await tester2.JoinPlace(place.Id);
        var chatAuthor2 = await tester1.InviteToChat(chat.Id, account2.Id);

        // act
        await tester1.PromoteToOwner(chatAuthor2.Id);
        var ownerIds = await roles.ListOwnerIds(session1, chat.Id, default);
        ownerIds.Should().BeEquivalentTo([chatAuthor1.Id, chatAuthor2.Id]);
        await tester1.LeaveChat(chat.Id);

        // assert
        await TestExt.When(async () => {
                var chatMembers1 = await authors.ListUserIds(session1, chat.Id, default);
                chatMembers1.Should().BeEmpty();
                var chatMembers2 = await authors.ListUserIds(session2, chat.Id, default);
                chatMembers2.Should().BeEquivalentTo([account2.Id]);

                var placeIds1 = await contacts.ListPlaceIds(session1, default);
                var placeIds2 = await contacts.ListPlaceIds(session2, default);
                placeIds1.Should().BeEquivalentTo(placeIds2).And.BeEquivalentTo([place.Id]);
            },
            TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ItShouldBeNotPossibleToLeavePublicChatOnPlace(bool isPublicPlace)
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        var session = tester.Session;
        await tester.SignInAsUniqueBob();

        var services = tester.AppServices;
        var chats = services.GetRequiredService<IChats>();
        var commander = tester.Commander;

        var (_, chat) = await CreatePlaceWithDefaultChat(commander, session, isPublicPlace: isPublicPlace);

        await ComputedTest.When(async ct => {
            chat = await chats.Get(session, chat.Id, ct);
            chat.Should().NotBeNull();
        });

        chat.IsPublic.Should().BeTrue();
        chat.Rules.CanLeave().Should().BeFalse();

        await Assert.ThrowsAsync<SecurityException>(() =>
            commander.Call(new Authors_Leave { Session = session, ChatId = chat.Id }
        ));
    }

    [Fact]
    public async Task UpsertTextEntry()
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        var session1 = tester.Session;
        await tester.SignInAsUniqueBob();

        var commander1 = tester.Commander;

        var (place, chat) = await CreatePlaceWithDefaultChat(commander1, session1);

        await using var tester2 = AppHost.NewBlazorTester(Out);
        var session2 = tester2.Session;
        await tester2.SignInAsUniqueAlice();
        var commander2 = tester2.Commander;

        await commander2.Call(new Places_Join { Session = session2, PlaceId = place.Id });

        var cmd1 = new Chats_UpsertEntry {
            Session = session1,
            ChatId = chat.Id,
            LocalId = null,
            Text = "My first message",
        };
        var chatEntry1 = await commander1.Call(cmd1);
        chatEntry1.Should().NotBeNull();

        var cmd2 = new Chats_UpsertEntry {
            Session = session2,
            ChatId = chat.Id,
            LocalId = null,
            Text = "And mine first message",
        };
        var chatEntry2 = await commander2.Call(cmd2);
        chatEntry2.Should().NotBeNull();
    }

    [Fact]
    public async Task UpsertTextEntryToPublicPlaceChatShouldEnsureThatExplicitAuthorExist()
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        var session1 = tester.Session;
        await tester.SignInAsUniqueBob();

        var commander1 = tester.Commander;

        var (place, chat) = await CreatePlaceWithDefaultChat(commander1, session1);

        await using var tester2 = AppHost.NewBlazorTester(Out);
        var session2 = tester2.Session;
        await tester2.SignInAsUniqueAlice();

        var account = await tester2.AppServices.GetRequiredService<IAccounts>().GetOwn(session2, default);
        await commander1.Call(new Places_Invite { Session = session1, PlaceId = place.Id, UserIds = [account.Id] });

        var commander2 = tester2.Commander;
        var cmd = new Chats_UpsertEntry {
            Session = session2,
            ChatId = chat.Id,
            LocalId = null,
            Text = "My first message",
        };
        var chatEntry = await commander2.Call(cmd);
        chatEntry.Should().NotBeNull();
        var authorId = chatEntry.AuthorId;

        var authorsBackend = tester2.AppServices.GetRequiredService<IAuthorsBackend>();
        var explicitAuthor = await authorsBackend.Get(authorId.ChatId, authorId, RequestedAuthorKind.Default, default);
        explicitAuthor.Should().NotBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NonPlaceMembersShouldBeAbleToReadPublicPlacesOnly(bool isPublicPlace)
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        var session1 = tester.Session;
        await tester.SignInAsUniqueBob();
        var commander1 = tester.Commander;

        var place = await CreatePlace(commander1, session1, isPublicPlace);

        await using var tester2 = AppHost.NewBlazorTester(Out);
        var session2 = tester2.Session;
        await tester2.SignInAsUniqueAlice();
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
        await using var tester = AppHost.NewBlazorTester(Out);
        var session1 = tester.Session;
        await tester.SignInAsUniqueBob();

        var commander1 = tester.Commander;

        var place = await CreatePlace(commander1, session1, true);

        await using var tester2 = AppHost.NewBlazorTester(Out);
        var session2 = tester2.Session;
        await tester2.SignInAsUniqueAlice();
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
        await using var tester = AppHost.NewBlazorTester(Out);
        var session1 = tester.Session;
        await tester.SignInAsUniqueBob();

        var commander1 = tester.Commander;

        var place = await CreatePlace(commander1, session1, false);

        await using var tester2 = AppHost.NewBlazorTester(Out);
        var session2 = tester2.Session;
        await tester2.SignInAsUniqueAlice();
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
        await using var tester = AppHost.NewBlazorTester(Out);
        var session1 = tester.Session;
        await tester.SignInAsUniqueBob();

        var commander1 = tester.Commander;

        var place = await CreatePlace(commander1, session1, true);

        await using var tester2 = AppHost.NewBlazorTester(Out);
        var session2 = tester2.Session;
        await tester2.SignInAsUniqueAlice();
        var commander2 = tester2.Commander;

        await commander2.Call(new Places_Join { Session = session2, PlaceId = place.Id });

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
    [InlineData(true)]
    [InlineData(false)]
    public async Task UserShouldNotBeListedInThePlaceChatAfterRemovingFromPlace(bool isPublicChat)
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        var session1 = tester.Session;
        await tester.SignInAsUniqueBob();

        var commander1 = tester.Commander;
        var authors = tester.ScopedAppServices.GetRequiredService<IAuthors>();

        var place = await CreatePlace(commander1, session1, false);
        var chat = await CreateChat(commander1, session1, place.Id, isPublicChat);

        await using var tester2 = AppHost.NewBlazorTester(Out);
        var session2 = tester2.Session;
        await tester2.SignInAsUniqueAlice();
        var commander2 = tester2.Commander;
        var accounts2 = tester2.ScopedAppServices.GetRequiredService<IAccounts>();
        var user2 = await accounts2.GetOwn(session2, default);

        await commander1.Call(new Places_Invite { Session = session1, PlaceId = place.Id, UserIds = [user2.Id] });
        var placeFromUser2Perspective = await tester2.Places.Get(session2, place.Id, default).Require();
        var user2PlaceMember = placeFromUser2Perspective.Rules.Author.Require();

        var placeMembers = await tester.Places.ListAuthorIds(session1, place.Id, default);
        placeMembers.Should().HaveCount(2).And.Contain(user2PlaceMember.Id);
        var placeUsers = await tester.Places.ListUserIds(session1, place.Id, default);
        placeUsers.Should().HaveCount(2).And.Contain(user2.Id);

        if (!chat.IsPublic)
            await commander1.Call(new Authors_Invite { Session = session1, ChatId = chat.Id, UserIds = [user2.Id] });

        var chatFromUser2Perspective = await tester2.Chats.Get(session2, chat.Id, default).Require();
        var user2ChatAuthor = chatFromUser2Perspective.Rules.Author.Require();
        var chatMembers = await authors.ListAuthorIds(session1, chat.Id, default);
        chatMembers.Should().HaveCount(2).And.Contain(user2ChatAuthor.Id);
        var chatUsers = await authors.ListUserIds(session1, chat.Id, default);
        chatUsers.Should().HaveCount(2).And.Contain(user2.Id);

        // NOTE: user2 should write a message to ensure explicit author exists for the chat.
        var cmd = new Chats_UpsertEntry { Session = session2, ChatId = chat.Id, LocalId = null, Text = "Hello!" };
        await commander2.Call(cmd);

        await commander1.Call(new Places_Exclude { Session = session1, AuthorId = user2PlaceMember.Id });

        placeMembers = await tester.Places.ListAuthorIds(session1, place.Id, default);
        placeMembers.Should().HaveCount(1).And.NotContain(user2PlaceMember.Id);

        placeUsers = await tester.Places.ListUserIds(session1, place.Id, default);
        placeUsers.Should().HaveCount(1).And.NotContain(user2.Id);

        await TestExt.When(async () => {
            chatMembers = await authors.ListAuthorIds(session1, chat.Id, default);
            chatMembers.Should().HaveCount(1).And.NotContain(user2ChatAuthor.Id);
        }, TimeSpan.FromSeconds(10));

        chatUsers = await authors.ListUserIds(session1, chat.Id, default);
        chatUsers.Should().HaveCount(1).And.NotContain(user2.Id);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task OnlyPlaceOwnerShouldBeAbleToSwitchChatFromPrivateToPublic(bool isOwner, bool shouldSucceed)
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        var session1 = tester.Session;
        await tester.SignInAsUniqueBob();

        var commander1 = tester.Commander;

        var place = await CreatePlace(commander1, session1, true);
        var (session, commander) = (session1, commander1);

        if (!isOwner) {
            await using var tester2 = AppHost.NewBlazorTester(Out);
            var session2 = tester2.Session;
            await tester2.SignInAsUniqueAlice();
            var commander2 = tester2.Commander;

            await commander2.Call(new Places_Join { Session = session2, PlaceId = place.Id });
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
            return commander.Call(new Chats_Change {
                Session = session,
                ChatId = chat.Id,
                ExpectedVersion = null,
                Change = new () {
                    Update = new ChatDiff {
                        IsPublic = true,
                    },
                },
            });
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangeAvatar(bool isPublicChat)
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        var session1 = tester.Session;
        await tester.SignInAsUniqueBob();
        var commander1 = tester.Commander;
        var authors = tester.AppServices.GetRequiredService<IAuthors>();
        var accounts = tester.AppServices.GetRequiredService<IAccounts>();

        var account = await accounts.GetOwn(session1, default);
        var avatar1 = await CreateAvatar("Avatar 1");
        var avatar2 = await CreateAvatar("Avatar 2");

        var (_, chat) = await CreatePlaceWithDefaultChat(commander1, session1, false, isPublicChat);

        await commander1.Call(new Authors_SetAvatar { Session = session1, ChatId = chat.Id, AvatarId = avatar2.Id });
        var author = await authors.GetOwn(session1, chat.Id, default).Require();
        author.AvatarId.Should().Be(avatar2.Id);

        await commander1.Call(new Authors_SetAvatar { Session = session1, ChatId = chat.Id, AvatarId = avatar1.Id });
        author = await authors.GetOwn(session1, chat.Id, default).Require();
        author.AvatarId.Should().Be(avatar1.Id);

        async Task<AvatarFull> CreateAvatar(string name)
        {
            return await commander1.Call(new Avatars_Change {
                Session = session1,
                AvatarId = Symbol.Empty,
                ExpectedVersion = null,
                Change = Change.Create(new AvatarDiff {
                    Name = name
                }),
            });
        }
    }

    [Fact]
    public async Task UserShouldBeAbleToSeePublicPlaceChatListAfterLeavingThePlace()
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        var session1 = tester.Session;
        await tester.SignInAsUniqueBob();
        var commander1 = tester.Commander;

        var place = await CreatePlace(commander1, session1, true);
        var placeId = place.Id;
        var chat = await CreateChat(commander1, session1, placeId, true);
        var chatId = chat.Id;

        await using var tester2 = AppHost.NewBlazorTester(Out);
        var session2 = tester2.Session;
        await tester2.SignInAsUniqueAlice();
        var accounts2 = tester2.ScopedAppServices.GetRequiredService<IAccounts>();
        var user2 = await accounts2.GetOwn(session2, default);

        var chatFromUser2Perspective = await tester2.Chats.Get(session2, chatId, default);
        chatFromUser2Perspective.Should().NotBeNull();

        await commander1.Call(new Places_Invite { Session = session1, PlaceId = placeId, UserIds = [user2.Id] });
        var placeFromUser2Perspective = await tester2.Places.Get(session2, placeId, default).Require();
        var user2PlaceMember = placeFromUser2Perspective.Rules.Author.Require();

        var placeMembers = await tester.Places.ListAuthorIds(session1, placeId, default);
        placeMembers.Should().HaveCount(2).And.Contain(user2PlaceMember.Id);

        var contacts = tester2.AppServices.GetRequiredService<IContacts>();
        var contactIds = await contacts.ListIds(session2, placeId, default);
        contactIds.Should().HaveCount(1);
        contactIds[0].ChatId.Should().Be(chatId);

        await commander1.Call(new Places_Exclude { Session = session1, AuthorId = user2PlaceMember.Id });

        await ComputedTest.When(async ct => {
            placeFromUser2Perspective = await tester2.Places.Get(session2, placeId, ct).Require();
            placeFromUser2Perspective.Rules.Author.Require();
            placeFromUser2Perspective.Rules.Author.HasLeft.Should().BeTrue();
            placeFromUser2Perspective.Rules.CanJoin().Should().BeTrue();
        });

        contactIds = await contacts.ListIds(session2, placeId, default);
        contactIds.Should().HaveCount(1);
        contactIds[0].ChatId.Should().Be(chatId);

        chatFromUser2Perspective = await tester2.Chats.Get(session2, chatId, default);
        chatFromUser2Perspective.Should().NotBeNull();

        var chatContactForUser2 = await contacts.GetForChat(session2, chatId, default);
        chatContactForUser2.Should().NotBeNull();
    }

    // TODO(DF): fix it
    [Fact(Skip = "Flaky")]
    public async Task UserShouldBeAbleToRejoinPlacePrivateChatOnlyAfterRejoingPlace()
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        var session1 = tester.Session;
        await tester.SignInAsUniqueBob();

        var commander1 = tester.Commander;
        var chats = tester.ScopedAppServices.GetRequiredService<IChats>();
        var invites = tester.ScopedAppServices.GetRequiredService<IInvites>();

        var place = await CreatePlace(commander1, session1, true);
        var chat = await CreateChat(commander1, session1, place.Id, false);
        var invite = await invites.GetOrGenerateChatInvite(session1, chat.Id, default).Require();

        await using var tester2 = AppHost.NewBlazorTester(Out);
        var session2 = tester2.Session;
        await tester2.SignInAsUniqueAlice();
        var commander2 = tester2.Commander;
        var accounts2 = tester2.ScopedAppServices.GetRequiredService<IAccounts>();
        var user2 = await accounts2.GetOwn(session2, default);

        await commander1.Call(new Places_Invite { Session = session1, PlaceId = place.Id, UserIds = [user2.Id] });
        var placeFromUser2Perspective = await tester2.Places.Get(session2, place.Id, default).Require();
        var user2PlaceMember = placeFromUser2Perspective.Rules.Author.Require();

        var placeMembers = await tester.Places.ListAuthorIds(session1, place.Id, default);
        placeMembers.Should().HaveCount(2).And.Contain(user2PlaceMember.Id);

        await tester2.JoinChat(chat.Id, invite.Id);

        await commander2.Call(new Places_Leave { Session = session2, PlaceId = place.Id });

        var chatRules = await chats.GetRules(session2, chat.Id, default);

        var canJoin = chatRules.CanJoin();
        canJoin.Should().BeFalse();

        await commander1.Call(new Places_Invite { Session = session1, PlaceId = place.Id, UserIds = [user2.Id] });

        await commander2.Call(new Invites_Use { Session = session2, InviteId = invite.Id }, true);

        await ComputedTest.When(async ct => {
            chatRules = await chats.GetRules(session2, chat.Id, ct);
            canJoin = chatRules.CanJoin();
            canJoin.Should().BeTrue();
        });
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
        var place = await commander.Call(new Places_Change {
            Session = session,
            PlaceId = default,
            ExpectedVersion = null,
            Change = new () {
                Create = new PlaceDiff {
                    Title = PlaceTitle,
                    IsPublic = isPublicPlace,
                },
            },
        });
        return place;
    }

    private static async Task<Chat> CreateChat(
        ICommander commander,
        Session session,
        PlaceId placeId,
        bool isPublicChat,
        string chatTitle = ChatTitle)
    {
        var chat = await commander.Call(new Chats_Change {
            Session = session,
            ChatId = default,
            ExpectedVersion = null,
            Change = new () {
                Create = new ChatDiff {
                    Title = chatTitle,
                    IsPublic = isPublicChat,
                    PlaceId = placeId,
                },
            },
        });
        return chat;
    }
}
