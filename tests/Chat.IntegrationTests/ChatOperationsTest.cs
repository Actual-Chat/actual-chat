using ActualChat.Testing.Host;
using ActualChat.Invite;
using ActualChat.Users;

namespace ActualChat.Chat.IntegrationTests;

public class ChatOperationsTest : AppHostTestBase
{
    public ChatOperationsTest(ITestOutputHelper @out) : base(@out) { }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateNewChat(bool isPublicChat)
    {
        using var appHost = await NewAppHost();
        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        var account = await tester.SignIn(new User("", "Bob"));

        var services = tester.AppServices;
        var chats = services.GetRequiredService<IChats>();
        var roles = services.GetRequiredService<IRoles>();
        var authors = services.GetRequiredService<IAuthors>();
        var commander = tester.Commander;

        var chatTitle = "test chat";
        var chat = await commander.Call(new IChats.ChangeCommand(session, default, null, new() {
            Create = new ChatDiff() {
                Title = chatTitle,
                Kind = ChatKind.Group,
                IsPublic = isPublicChat,
            },
        }));
        chat = chat.Require();
        await Task.Delay(100); // Let's wait invalidations to hit the client
        chat = await chats.Get(session, chat.Id, default).Require();

        chat.Should().NotBeNull();
        chat.Title.Should().Be(chatTitle);
        chat.IsPublic.Should().Be(isPublicChat);

        var chatRoles = await roles.List(session, chat.Id, default);
        chatRoles.Length.Should().Be(2);

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
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JoinChat(bool isPublicChat)
    {
        using var appHost = await NewAppHost();

        ChatId chatId;
        var inviteId = Symbol.Empty;
        {
            await using var tester = appHost.NewBlazorTester();
            var session = tester.Session;
            await tester.SignIn(new User("", "Alice"));

            var commander = tester.Commander;
            var chat = await commander.Call(new IChats.ChangeCommand(session, default, null, new() {
                Create = new ChatDiff() {
                    Title = "test chat",
                    Kind = ChatKind.Group,
                    IsPublic = isPublicChat,
                },
            }));
            chat = chat.Require();
            chatId = chat.Id;

            if (!isPublicChat) {
                // to join private chat we need to generate invite code
                var invite = new Invite.Invite {
                    Details = new InviteDetails { Chat = new (chatId) },
                    Remaining = 10,
                };
                invite = await commander.Call(new IInvites.GenerateCommand(session, invite));
                inviteId = invite.Id;
            }
        }

        {
            await using var tester = appHost.NewBlazorTester();
            var session = tester.Session;
            var account = await tester.SignIn(new User("", "Bob").WithIdentity("no-admin"));
            var commander = tester.Commander;
            var chats = tester.AppServices.GetRequiredService<IChats>();
            var canJoin = await chats.CanJoin(session, chatId, default);

            if (!isPublicChat) {
                canJoin.Should().BeFalse();
                // to join private chat we need to activate invite code first
                await commander.Call(new IInvites.UseCommand(session, inviteId));

                var c = await Computed.Capture(() => chats.CanJoin(session, chatId, default));
                await c.When(x => x).WaitAsync(TimeSpan.FromSeconds(3));
                canJoin = await c.Use();
            }
            canJoin.Should().BeTrue();

            var command = new IChats.JoinCommand(session, chatId);
            await commander.Call(command, default);

            await AssertJoined(tester.AppServices, session, chatId, account);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Leave(bool isPublicChat)
    {
        using var appHost = await NewAppHost();

        ChatId chatId;
        Symbol inviteId = Symbol.Empty;
        {
            await using var tester = appHost.NewBlazorTester();
            var session = tester.Session;
            await tester.SignIn(new User("", "Alice"));

            var commander = tester.Commander;
            var chat = await commander.Call(new IChats.ChangeCommand(session, default, null, new() {
                Create = new ChatDiff() {
                    Title = "test chat",
                    Kind = ChatKind.Group,
                    IsPublic = isPublicChat,
                },
            }));
            chat = chat.Require();
            chatId = chat.Id;

            if (!isPublicChat) {
                // to join private chat we need to generate invite code
                var invite = new Invite.Invite {
                    Details = new InviteDetails { Chat = new (chatId) },
                    Remaining = 10,
                };
                invite = await commander.Call(new IInvites.GenerateCommand(session, invite));
                inviteId = invite.Id;
            }
        }

        {
            await using var tester = appHost.NewBlazorTester();
            var session = tester.Session;
            var account = await tester.SignIn(new User("", "Bob").WithIdentity("no-admin"));
            var commander = tester.Commander;
            var chats = tester.AppServices.GetRequiredService<IChats>();

            if (!isPublicChat) {
                await commander.Call(new IInvites.UseCommand(session, inviteId));
                await Task.Delay(1000); // Let the command complete
            }

            var joinChatCommand = new IChats.JoinCommand(session, chatId);
            await commander.Call(joinChatCommand);
            await AssertJoined(tester.AppServices, session, chatId, account);

            var leaveCommand = new IChats.LeaveCommand(session, chatId);
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
                await commander.Call(new IInvites.UseCommand(session, inviteId));
                await Task.Delay(1000); // Let the command complete
            }
            await commander.Call(joinChatCommand, default);
            await AssertJoined(tester.AppServices, session, chatId, account);
        }
    }

    private static async Task AssertJoined(IServiceProvider services, Session session, ChatId chatId, Account account)
    {
        var chats = services.GetRequiredService<IChats>();

        var cRules = await Computed.Capture(() => chats.GetRules(session, chatId, default));
        await cRules.When(r => r.CanRead() && r.CanWrite())
            .WaitAsync(TimeSpan.FromSeconds(3));
        var rules = await cRules.Use();
        rules.CanRead().Should().BeTrue();
        rules.CanWrite().Should().BeTrue();

        var chat = await chats.Get(session, chatId, default);
        chat.Should().NotBeNull();
        var authors = services.GetRequiredService<IAuthors>();
        var authorsUpgradeBackend = services.GetRequiredService<IAuthorsUpgradeBackend>();
        var author = await authors.GetOwn(session, chatId, default);
        author.Should().NotBeNull();
        author!.UserId.Should().Be(account.Id);
        author.HasLeft.Should().BeFalse();

        var ownChatIds = await authorsUpgradeBackend.ListOwnChatIds(session, default);
        ownChatIds.Should().Contain(chatId);

        var userIds = await authors.ListUserIds(session, chatId, default);
        userIds.Should().Contain(account.Id);
        var authorIds = await authors.ListAuthorIds(session, chatId, default);
        authorIds.Should().Contain(author.Id);
    }

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
