using ActualChat.Testing.Host;
using ActualChat.Invite;

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
        var user = await tester.SignIn(new User("", "Bob"));

        var services = tester.AppServices;
        var chats = services.GetRequiredService<IChats>();
        var chatRoles = services.GetRequiredService<IChatRoles>();
        var chatAuthors = services.GetRequiredService<IChatAuthors>();
        var commander = tester.Commander;

        var chatTitle = "test chat";
        var chat = await commander.Call(new IChats.ChangeCommand(session, "", null, new() {
            Create = new ChatDiff() {
                Title = chatTitle,
                ChatType = ChatType.Group,
                IsPublic = isPublicChat,
            },
        }));
        chat = chat.Require();
        await Task.Delay(100); // Let's wait invalidations to hit the client
        chat = await chats.Get(session, chat.Id, default).Require();

        chat.Should().NotBeNull();
        chat.Title.Should().Be(chatTitle);
        chat.IsPublic.Should().Be(isPublicChat);

        var roles = await chatRoles.List(session, chat.Id, default);
        roles.Length.Should().Be(2);

        var owners = roles.Single(r => r.SystemRole is SystemChatRole.Owner);
        owners.Name.Should().Be(SystemChatRole.Owner.ToString());
        owners.Permissions.Has(ChatPermissions.Owner).Should().BeTrue();

        var joined = roles.Single(r => r.SystemRole is SystemChatRole.Anyone);
        joined.Name.Should().Be(SystemChatRole.Anyone.ToString());
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

        var author = await chatAuthors.GetOwn(session, chat.Id, default);
        author.Should().NotBeNull();
        author!.UserId.Should().Be(user.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JoinChat(bool isPublicChat)
    {
        using var appHost = await NewAppHost();

        Symbol chatId;
        Symbol inviteId = Symbol.Empty;
        {
            await using var tester = appHost.NewBlazorTester();
            var session = tester.Session;
            await tester.SignIn(new User("", "Alice"));

            var commander = tester.Commander;
            var chat = await commander.Call(new IChats.ChangeCommand(session, "", null, new() {
                Create = new ChatDiff() {
                    Title = "test chat",
                    ChatType = ChatType.Group,
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
            var user = await tester.SignIn(new User("", "Bob").WithIdentity("no-admin"));
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

            await AssertUserJoined(tester.AppServices, session, chatId, user);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Leave(bool isPublicChat)
    {
        using var appHost = await NewAppHost();

        Symbol chatId;
        Symbol inviteId = Symbol.Empty;
        {
            await using var tester = appHost.NewBlazorTester();
            var session = tester.Session;
            await tester.SignIn(new User("", "Alice"));

            var commander = tester.Commander;
            var chat = await commander.Call(new IChats.ChangeCommand(session, "", null, new() {
                Create = new ChatDiff() {
                    Title = "test chat",
                    ChatType = ChatType.Group,
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
            var user = await tester.SignIn(new User("", "Bob").WithIdentity("no-admin"));
            var commander = tester.Commander;
            var chats = tester.AppServices.GetRequiredService<IChats>();

            if (!isPublicChat) {
                await commander.Call(new IInvites.UseCommand(session, inviteId));
                await Task.Delay(1000); // Let the command complete
            }

            var joinChatCommand = new IChats.JoinCommand(session, chatId);
            await commander.Call(joinChatCommand);
            await AssertUserJoined(tester.AppServices, session, chatId, user);

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

            await AssertUserNotJoined(tester.AppServices, session, chatId, user);

            // re-join again
            if (!isPublicChat) {
                await commander.Call(new IInvites.UseCommand(session, inviteId));
                await Task.Delay(1000); // Let the command complete
            }
            await commander.Call(joinChatCommand, default);
            await AssertUserJoined(tester.AppServices, session, chatId, user);
        }
    }

    private static async Task AssertUserJoined(IServiceProvider services, Session session, Symbol chatId, User user)
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
        var chatAuthors = services.GetRequiredService<IChatAuthors>();
        var author = await chatAuthors.GetOwn(session, chatId, default);
        author.Should().NotBeNull();
        author!.UserId.Should().Be(user.Id);
        author.HasLeft.Should().BeFalse();

        var ownChatIds = await chatAuthors.ListChatIds(session, default);
        ownChatIds.Should().Contain(chatId);

        var userIds = await chatAuthors.ListUserIds(session, chatId, default);
        userIds.Should().Contain(user.Id);
        var authorIds = await chatAuthors.ListAuthorIds(session, chatId, default);
        authorIds.Should().Contain(author.Id);
    }

    private static async Task AssertUserNotJoined(IServiceProvider services, Session session, Symbol chatId, User user)
    {
        var chatAuthors = services.GetRequiredService<IChatAuthors>();
        var chatAuthorsBackend = services.GetRequiredService<IChatAuthorsBackend>();

        var cAuthor = await Computed.Capture(() => chatAuthors.GetOwn(session, chatId, default));
        await cAuthor.When(a => a is { HasLeft: true })
            .WaitAsync(TimeSpan.FromSeconds(3));
        var author = await cAuthor.Use();
        author!.HasLeft.Should().BeTrue();

        var ownChatIds = await chatAuthors.ListChatIds(session, default);
        ownChatIds.Should().NotContain(chatId);

        var userIds = await chatAuthors.ListUserIds(session, chatId, default);
        userIds.Should().NotContain(user.Id);
        // use backend service to avoid permissions check
        var authorIds = await chatAuthorsBackend.ListAuthorIds(chatId, default);
        authorIds.Should().NotContain(author.Id);
    }
}
