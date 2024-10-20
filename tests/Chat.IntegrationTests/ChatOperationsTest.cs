using ActualChat.Chat.Db;
using ActualChat.Chat.Module;
using ActualChat.Contacts;
using ActualChat.Testing.Host;
using ActualChat.Invite;
using ActualChat.Queues;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ChatOperationsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateNewChat(bool isPublicChat)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var session = tester.Session;
        var account = await tester.SignInAsBob();

        var services = tester.AppServices;
        var chats = services.GetRequiredService<IChats>();
        var roles = services.GetRequiredService<IRoles>();
        var authors = services.GetRequiredService<IAuthors>();
        var commander = tester.Commander;

        var chatTitle = "test chat";
        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff() {
                Title = chatTitle,
                Kind = ChatKind.Group,
                IsPublic = isPublicChat,
            },
        }));
        chat.Require();
        await Task.Delay(100); // Let's wait invalidations to hit the client
        chat = await chats.Get(session, chat.Id, default).Require();

        chat.Should().NotBeNull();
        chat.Title.Should().Be(chatTitle);
        chat.IsPublic.Should().Be(isPublicChat);

        var chatRoles = await roles.List(session, chat.Id, default);
        chatRoles.Count.Should().Be(2);

        var owners = chatRoles.Single(r => r.SystemRole is SystemRole.Owner);
        owners.Name.Should().Be(SystemRole.Owner.ToString());
        owners.Permissions.Has(ChatPermissions.Owner).Should().BeTrue();

        var joined = chatRoles.Single(r => r.SystemRole is SystemRole.Anyone);
        joined.Name.Should().Be(SystemRole.Anyone.ToString());
        joined.Permissions.Has(ChatPermissions.Read).Should().BeTrue();
        joined.Permissions.Has(ChatPermissions.Write).Should().BeTrue();
        joined.Permissions.Has(ChatPermissions.Join).Should().BeTrue();
        joined.Permissions.Has(ChatPermissions.Invite).Should().BeTrue();

        chat.Should().NotBeNull();

        var rules = await chats.GetRules(session, chat.Id, default);
        rules.CanRead().Should().BeTrue();
        rules.CanWrite().Should().BeTrue();
        rules.CanJoin().Should().BeTrue();
        rules.CanInvite().Should().BeTrue();
        rules.CanEditProperties().Should().BeTrue();
        rules.CanEditRoles().Should().BeTrue();
        rules.IsOwner().Should().BeTrue();

        var author = await authors.GetOwn(session, chat.Id, default);
        author.Should().NotBeNull();
        author!.UserId.Should().Be(account.Id);
        author.Avatar.Should().NotBeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task ShouldGetPlaceChat(bool isPublicPlace, bool isPublicChat)
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        var session = tester.Session;
        var account = await tester.SignInAsBob();

        var services = tester.AppServices;
        var chats = services.GetRequiredService<IChats>();
        var roles = services.GetRequiredService<IRoles>();
        var authors = services.GetRequiredService<IAuthors>();

        var place = await tester.CreatePlace(isPublicPlace);

        // act
        var (chatId, _) = await tester.CreateChat(isPublicChat, "Some chat", place.Id);
        var chat = await chats.Get(session, chatId, default).Require();

        chat.Should().NotBeNull();
        chat.Title.Should().Be("Some chat");
        chat.IsPublic.Should().Be(isPublicChat);
        chat.Rules.Author.Should().NotBeNull();
        chat.Rules.Author!.Avatar.Should().NotBeNull();

        var chatRoles = await roles.List(session, chat.Id, default);
        chatRoles.Count.Should().Be(2);

        var owners = chatRoles.Single(r => r.SystemRole is SystemRole.Owner);
        owners.Name.Should().Be(SystemRole.Owner.ToString());
        owners.Permissions.Has(ChatPermissions.Owner).Should().BeTrue();

        var joined = chatRoles.Single(r => r.SystemRole is SystemRole.Anyone);
        joined.Name.Should().Be(SystemRole.Anyone.ToString());
        joined.Permissions.Has(ChatPermissions.Read).Should().BeTrue();
        joined.Permissions.Has(ChatPermissions.Write).Should().BeTrue();
        joined.Permissions.Has(ChatPermissions.Join).Should().BeTrue();
        joined.Permissions.Has(ChatPermissions.Invite).Should().BeTrue();

        var rules = await chats.GetRules(session, chat.Id, default);
        rules.CanRead().Should().BeTrue();
        rules.CanWrite().Should().BeTrue();
        rules.CanJoin().Should().BeTrue();
        rules.CanInvite().Should().BeTrue();
        rules.CanEditProperties().Should().BeTrue();
        rules.CanEditRoles().Should().BeTrue();
        rules.IsOwner().Should().BeTrue();

        var author = await authors.GetOwn(session, chat.Id, default);
        rules.Author.Should().BeEquivalentTo(author);
        author.Should().NotBeNull();
        author!.UserId.Should().Be(account.Id);
        author.Avatar.Should().NotBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateNewChatBackend(bool isPublicChat)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var session = tester.Session;
        var account = await tester.SignInAsBob();

        var services = tester.AppServices;
        var chatsBackend = services.GetRequiredService<IChatsBackend>();
        var roles = services.GetRequiredService<IRoles>();
        var authors = services.GetRequiredService<IAuthors>();
        var commander = tester.Commander;

        var chatTitle = "test chat 2";
        var chat = await commander.Call(new ChatsBackend_Change(ChatId.None, default,  new() {
            Create = new ChatDiff() {
                Title = chatTitle,
                Kind = ChatKind.Group,
                IsPublic = isPublicChat,
            },
        }, account.Id));
        chat.Require();
        await Task.Delay(100); // Let's wait invalidations to hit the client
        chat = await chatsBackend.Get(chat.Id, default).Require();

        chat.Should().NotBeNull();
        chat.Title.Should().Be(chatTitle);
        chat.IsPublic.Should().Be(isPublicChat);

        var chatRoles = await roles.List(session, chat.Id, default);
        chatRoles.Count.Should().Be(2);

        var owners = chatRoles.Single(r => r.SystemRole is SystemRole.Owner);
        owners.Name.Should().Be(SystemRole.Owner.ToString());
        owners.Permissions.Has(ChatPermissions.Owner).Should().BeTrue();

        var joined = chatRoles.Single(r => r.SystemRole is SystemRole.Anyone);
        joined.Name.Should().Be(SystemRole.Anyone.ToString());
        joined.Permissions.Has(ChatPermissions.Read).Should().BeTrue();
        joined.Permissions.Has(ChatPermissions.Write).Should().BeTrue();
        joined.Permissions.Has(ChatPermissions.Join).Should().BeTrue();
        joined.Permissions.Has(ChatPermissions.Invite).Should().BeTrue();

        chat.Should().NotBeNull();

        var rules = await chatsBackend.GetRules(chat.Id, new PrincipalId(account.Id, AssumeValid.Option), default);
        rules.CanRead().Should().BeTrue();
        rules.CanWrite().Should().BeTrue();
        rules.CanJoin().Should().BeTrue();
        rules.CanInvite().Should().BeTrue();
        rules.CanEditProperties().Should().BeTrue();
        rules.CanEditRoles().Should().BeTrue();
        rules.IsOwner().Should().BeTrue();

        var author = await authors.GetOwn(session, chat.Id, default);
        author.Should().NotBeNull();
        author!.UserId.Should().Be(account.Id);
    }

    [Fact]
    public async Task NotesChatCreatedOnSignIn()
    {
        // arrange
        using var appHost = await NewAppHost("notes-chats", options => options with {
            ChatDbInitializerOptions = ChatDbInitializer.Options.Default with {
                AddNotesChat = true,
            },
        });
        await using var tester = appHost.NewBlazorTester(Out);
        var services = tester.AppServices;
        var chatsBackend = services.GetRequiredService<IChatsBackend>();
        var authors = services.GetRequiredService<IAuthors>();
        var contacts = services.GetRequiredService<IContacts>();
        var session = tester.Session;

        var account = await tester.SignInAsNew("NotesTest");
        account.Should().NotBeNull();

        // pre-assert wait
        await services.Queues().WhenProcessing();
        await ComputedTest.When(async ct => {
            var chats = (await (await contacts.ListIds(session, PlaceId.None, ct))
                .Select(x => chatsBackend.Get(x.ChatId, ct))
                .Collect(ct))
                .SkipNullItems()
                .ToList();
            chats.Any(c => c.SystemTag == Constants.Chat.SystemTags.Notes).Should().BeTrue();
        });

        // assert
        await TestExt.When(async () => {
            var dbHub = services.DbHub<ChatDbContext>();
            await using var dbContext = await dbHub.CreateDbContext();
            var dbChat = await dbContext.Chats
                .Join(dbContext.Authors, c => c.Id, a => a.ChatId, (c, a) => new { c, a })
                .Where(x => x.a.UserId == account.Id && x.c.SystemTag == Constants.Chat.SystemTags.Notes.Value)
                .Select(x => x.c)
                .FirstOrDefaultAsync();
            dbChat.Should().NotBeNull();
            var chat = await chatsBackend.Get(ChatId.Parse(dbChat!.Id), CancellationToken.None);
            chat.Require();
            chat.Should().NotBeNull();
            chat.Title.Should().Be("Notes");
            chat.IsPublic.Should().Be(false);
            var rules = await chatsBackend.GetRules(chat.Id, new PrincipalId(account.Id, AssumeValid.Option), default);
            rules.CanRead().Should().BeTrue();
            rules.CanWrite().Should().BeTrue();
            rules.CanJoin().Should().BeFalse();
            rules.CanInvite().Should().BeFalse();
            rules.CanEditProperties().Should().BeFalse();
            rules.CanEditRoles().Should().BeFalse();
            rules.IsOwner().Should().BeFalse();

            var author = await authors.GetOwn(session, chat.Id, default);
            author.Should().NotBeNull();
            author!.UserId.Should().Be(account.Id);
        }, TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JoinChat(bool isPublicChat)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);

        await tester.SignInAsAlice();
        var (chatId, inviteId) = await tester.CreateChat(isPublicChat);

        await tester.SignInAsBob("no-admin");

        await tester.JoinChat(chatId, inviteId);
        await tester.AssertJoined(chatId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Leave(bool isPublicChat)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);

        await tester.SignInAsAlice();
        var (chatId, inviteId) = await tester.CreateChat(isPublicChat);

        var session = tester.Session;
        var account = await tester.SignInAsBob("no-admin");
        var commander = tester.Commander;
        var chats = tester.AppServices.GetRequiredService<IChats>();
        var authors = tester.AppServices.GetRequiredService<IAuthors>();

        if (!isPublicChat) {
            await commander.Call(new Invites_Use(session, inviteId));
            await Task.Delay(1000); // Let the command complete
        }

        await authors.EnsureJoined(session, chatId, default);
        await tester.AssertJoined(chatId);

        var leaveCommand = new Authors_Leave(session, chatId);
        await commander.Call(leaveCommand);

        var permissions = await chats.GetRules(session, chatId, default);
        permissions.CanRead().Should().Be(isPublicChat);
        permissions.CanWrite().Should().BeFalse();

        var chat = await chats.Get(session, chatId, default);
        if (isPublicChat)
            chat.Should().NotBeNull();
        else
            chat.Should().BeNull();

        await AssertNotJoined(tester.AppServices, session, chatId, account);

        // re-join again
        if (!isPublicChat) {
            await commander.Call(new Invites_Use(session, inviteId));
            await Task.Delay(1000); // Let the command complete
        }
        await authors.EnsureJoined(session, chatId, default);
        await tester.AssertJoined(chatId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bob")]
    public async Task ShouldNotAllowJoinOrLeaveAnnouncementsChat(string? userName)
    {
        // arrange
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var chats = tester.AppServices.GetRequiredService<IChats>();
        var session = tester.Session;
        var chatId = Constants.Chat.AnnouncementsChatId;
        await tester.SignOut();
        if (!userName.IsNullOrEmpty())
            await tester.SignIn(new User(userName).WithIdentity("no-admin"));

        // act
        var rules = await chats.GetRules(session, chatId, default);

        //assert
        rules.CanJoin().Should().BeFalse();
        rules.CanLeave().Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PromoteAuthorToOwner(bool isPublicChat)
    {
        var appHost = AppHost;
        await using var ownerTester = appHost.NewBlazorTester(Out);
        await ownerTester.SignInAsAlice();

        var (chatId, inviteId) = await ownerTester.CreateChat(isPublicChat);

        await using var otherTester = appHost.NewBlazorTester(Out);
        await otherTester.SignInAsBob();

        var author = await otherTester.JoinChat(chatId, inviteId);

        var roles = otherTester.AppServices.GetRequiredService<IRoles>();
        var ownerIds = await roles.ListOwnerIds(otherTester.Session, chatId, default);
        ownerIds.Should().NotContain(author.Id);

        var chats = otherTester.AppServices.GetRequiredService<IChats>();
        var chat = await chats.Get(otherTester.Session, chatId, default);
        chat.Should().NotBeNull();
        chat!.Rules.IsOwner().Should().BeFalse();

        await ownerTester.Commander.Call(new Authors_PromoteToOwner(ownerTester.Session, author.Id));

        ownerIds = await roles.ListOwnerIds(otherTester.Session, chatId, default);
        ownerIds.Should().Contain(author.Id);

        chat = await chats.Get(otherTester.Session, chatId, default);
        chat.Should().NotBeNull();
        chat!.Rules.IsOwner().Should().BeTrue();
    }

    [Fact]
    public async Task NonOwnerUserShouldNotBeAblePromoteAuthorToOwner()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsAlice();
        var (chatId, inviteId) = await tester.CreateChat(true);

        await using var otherTester = appHost.NewBlazorTester(Out);
        await otherTester.SignInAsBob();

        var author = await otherTester.JoinChat(chatId, inviteId);

        var roles = otherTester.AppServices.GetRequiredService<IRoles>();
        var ownerIds = await roles.ListOwnerIds(otherTester.Session, chatId, default);
        ownerIds.Should().NotContain(author.Id);

        var chats = otherTester.AppServices.GetRequiredService<IChats>();
        var chat = await chats.Get(otherTester.Session, chatId, default);
        chat.Should().NotBeNull();
        chat!.Rules.IsOwner().Should().BeFalse();

        await Assert.ThrowsAsync<System.Security.SecurityException>(async () => {
            await otherTester.Commander.Call(new Authors_PromoteToOwner(otherTester.Session, author.Id));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheOnlyOwnerUserShouldNotBeAbleLeaveChat(bool isPublicChat)
    {
        var appHost = AppHost;
        await using var ownerTester = appHost.NewBlazorTester(Out);
        await ownerTester.SignInAsAlice();

        var (chatId, _) = await ownerTester.CreateChat(isPublicChat);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            await ownerTester.Commander.Call(new Authors_Leave(ownerTester.Session, chatId));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnerUserShouldBeAbleToLeaveChat(bool isPublicChat)
    {
        var appHost = AppHost;
        await using var ownerTester = appHost.NewBlazorTester(Out);
        await ownerTester.SignInAsAlice();

        var (chatId, inviteId) = await ownerTester.CreateChat(isPublicChat);

        await using var otherTester = appHost.NewBlazorTester(Out);
        await otherTester.SignInAsBob();

        var author = await otherTester.JoinChat(chatId, inviteId);

        await ownerTester.Commander.Call(new Authors_PromoteToOwner(ownerTester.Session, author.Id));

        await ownerTester.Commander.Call(new Authors_Leave(ownerTester.Session, chatId));
    }

    [Fact]
    public async Task ExOwnerUserBecomeRegularMemberAfterRejoining()
    {
        var appHost = AppHost;
        await using var ownerTester = appHost.NewBlazorTester(Out);
        await ownerTester.SignInAsAlice();

        var (chatId, inviteId) = await ownerTester.CreateChat(true);

        await using var otherTester = appHost.NewBlazorTester(Out);
        await otherTester.SignInAsBob();

        var author = await otherTester.JoinChat(chatId, inviteId);

        var commander = ownerTester.Commander;

        await commander.Call(new Authors_PromoteToOwner(ownerTester.Session, author.Id));

        await commander.Call(new Authors_Leave(ownerTester.Session, chatId));

        await ownerTester.JoinChat(chatId, inviteId);

        var chats = ownerTester.AppServices.GetRequiredService<IChats>();
        var chat = await chats.Get(ownerTester.Session, chatId, default);
        chat.Should().NotBeNull();
        chat!.Rules.IsOwner().Should().BeFalse();
    }

    [Fact]
    public async Task RemovingChatShouldRemoveItFromContactList()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        await using var ownerTester = appHost.NewBlazorTester(Out);
        await ownerTester.SignInAsAlice();
        var contacts = services.GetRequiredService<IContacts>();
        var session = ownerTester.Session;

        var (chatId, _) = await ownerTester.CreateChat(true);
        await ComputedTest.When(services, async ct => {
            var contactIds = await contacts.ListIds(session, PlaceId.None, ct);
            var chatIds = contactIds.Select(c => c.ChatId).ToArray();
            chatIds.Should().Contain(chatId);
        });

        var commander = services.Commander();
        var removeChatCommand = new Chats_Change(session, chatId, null, new Change<ChatDiff> { Remove = true });
        await commander.Call(removeChatCommand);

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, chatId, default);
        chat.Should().BeNull();

        await ComputedTest.When(services, async ct => {
            var contactIds = await contacts.ListIds(session, PlaceId.None, ct);
            var chatIds = contactIds.Select(c => c.ChatId).ToArray();
            chatIds.Should().NotContain(chatId);
        });
    }

    [Fact]
    public async Task ArchiveChat()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        await using var ownerTester = appHost.NewBlazorTester(Out);
        await ownerTester.SignInAsAlice();
        var contacts = services.GetRequiredService<IContacts>();
        var session = ownerTester.Session;

        var (chatId, inviteId) = await ownerTester.CreateChat(true);
        await ComputedTest.When(services, async ct => {
            var contactIds = await contacts.ListIds(session, PlaceId.None, ct);
            var chatIds = contactIds.Select(c => c.ChatId).ToArray();
            chatIds.Should().Contain(chatId);
        });

        await using var otherTester = appHost.NewBlazorTester(Out);
        await otherTester.SignInAsBob();
        var session2 = otherTester.Session;

        var author2 = await otherTester.JoinChat(chatId, inviteId);
        await ComputedTest.When(services, async ct => {
            var contactIds = await contacts.ListIds(session2, PlaceId.None, ct);
            var chatIds = contactIds.Select(c => c.ChatId).ToArray();
            chatIds.Should().Contain(chatId);
        });

        var commander = services.Commander();
        var archiveChatCommand = new Chats_Change(session, chatId, null, Change.Update(new ChatDiff {
            IsArchived = true
        }));
        await commander.Call(archiveChatCommand);

        // Owner should still see the chat (e.g. with direct link)
        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, chatId, default);
        chat.Should().NotBeNull();
        chat!.IsArchived.Should().BeTrue();
        chat.Rules.CanRead().Should().BeTrue();
        chat.Rules.CanWrite().Should().BeFalse();

        // But even the owner should not see in it contacts
        await ComputedTest.When(services, async ct => {
            var contactIds = await contacts.ListIds(session, PlaceId.None, ct);
            var chatIds = contactIds.Select(c => c.ChatId).ToArray();
            chatIds.Should().NotContain(chatId);
        });

        // Other participants should not see the chat
        chat = await chats.Get(session2, chatId, default);
        chat.Should().BeNull();

        await ComputedTest.When(services, async ct => {
            var contactIds = await contacts.ListIds(session2, PlaceId.None, ct);
            var chatIds = contactIds.Select(c => c.ChatId).ToArray();
            chatIds.Should().NotContain(chatId);
        });
    }

    // Private methods

    private static async Task AssertNotJoined(IServiceProvider services, Session session, ChatId chatId, Account account)
    {
        var authors = services.GetRequiredService<IAuthors>();
        var authorsBackend = services.GetRequiredService<IAuthorsBackend>();
        var authorsUpgradeBackend = services.GetRequiredService<IAuthorsUpgradeBackend>();

        var cAuthor = await Computed.Capture(() => authors.GetOwn(session, chatId, default));
        await cAuthor.When(a => a is { HasLeft: true })
            .WaitAsync(TimeSpan.FromSeconds(3));
        var author = await cAuthor.Use();
        author!.HasLeft.Should().BeTrue();

        var ownChatIds = await authorsUpgradeBackend.ListOwnChatIds(session, default);
        ownChatIds.Should().NotContain(chatId);

        var userIds = await authors.ListUserIds(session, chatId, default);
        userIds.Should().NotContain(account.Id);
        // use backend service to avoid permissions check
        var authorIds = await authorsBackend.ListAuthorIds(chatId, default);
        authorIds.Should().NotContain(author.Id);
    }
}
