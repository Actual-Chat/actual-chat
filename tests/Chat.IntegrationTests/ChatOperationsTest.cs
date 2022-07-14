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

        var commander = tester.Commander;
        var chatTitle = "test chat";
        var chatResult = await commander.Call(new IChats.CreateChatCommand(session, chatTitle) { IsPublic = isPublicChat });
        chatResult.Should().NotBeNull();
        chatResult.Title.Should().Be(chatTitle);
        chatResult.IsPublic.Should().Be(isPublicChat);
        chatResult.OwnerIds.Should().HaveCount(1);
        chatResult.OwnerIds.Should().Contain(user.Id);
        var chatId = chatResult.Id;

        var chats = tester.AppServices.GetRequiredService<IChats>();
        var chatAuthors = tester.AppServices.GetRequiredService<IChatAuthors>();
        var chat = await chats.Get(session, chatId, default);
        chat.Should().NotBeNull();

        var permissions = await chats.GetRules(session, chatId, default);
        permissions.CanReadWrite.Should().BeTrue();
        permissions.CanEditProperties.Should().BeTrue();
        permissions.CanInvite.Should().BeTrue();
        permissions.IsOwner.Should().BeTrue();

        var author = await chatAuthors.GetOwnAuthor(session, chatId, default);
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
            var chat = await commander.Call(new IChats.CreateChatCommand(session, "test chat") {
                IsPublic = isPublicChat
            });
            chatId = chat.Id;

            if (!isPublicChat) {
                // to join private chat we need to generate invite code
                var invite = new Invite.Invite {
                    Details = new InviteDetails { Chat = new (chatId) },
                    Remaining = 10
                };
                invite = await commander.Call(new IInvites.GenerateCommand(session, invite));
                inviteId = invite.Id;
            }
        }

        {
            await using var tester = appHost.NewBlazorTester();
            var session = tester.Session;
            var user = await tester.SignIn(new User("", "Bob"));
            var commander = tester.Commander;
            var chats = tester.AppServices.GetRequiredService<IChats>();
            var canJoin = await chats.CanJoin(session, chatId, default);

            if (!isPublicChat) {
                canJoin.Should().BeFalse();
                // to join private chat we need to activate invite code first
                var joinCommand = new IInvites.UseCommand(session, inviteId);
                await commander.Call(joinCommand);
                await WaitInviteActivationCompleted();
                canJoin = await chats.CanJoin(session, chatId, default);
            }

            canJoin.Should().BeTrue();

            var command = new IChats.JoinChatCommand(session, chatId);
            await commander.Call(command, default);

            await AssertUserJoined(tester.AppServices, session, chatId, user);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeaveChat(bool isPublicChat)
    {
        using var appHost = await NewAppHost();

        Symbol chatId;
        Symbol inviteId = Symbol.Empty;
        {
            await using var tester = appHost.NewBlazorTester();
            var session = tester.Session;
            await tester.SignIn(new User("", "Alice"));

            var commander = tester.Commander;
            var chat = await commander.Call(new IChats.CreateChatCommand(session, "test chat") {
                IsPublic = isPublicChat
            });
            chatId = chat.Id;

            if (!isPublicChat) {
                // to join private chat we need to generate invite code
                var invite = new Invite.Invite {
                    Details = new InviteDetails { Chat = new (chatId) },
                    Remaining = 10
                };
                invite = await commander.Call(new IInvites.GenerateCommand(session, invite));
                inviteId = invite.Id;
            }
        }

        {
            await using var tester = appHost.NewBlazorTester();
            var session = tester.Session;
            var user = await tester.SignIn(new User("", "Bob"));
            var commander = tester.Commander;
            var chats = tester.AppServices.GetRequiredService<IChats>();

            if (!isPublicChat) {
                var joinCommand = new IInvites.UseCommand(session, inviteId);
                await commander.Call(joinCommand);
                await WaitInviteActivationCompleted();
            }

            var command = new IChats.JoinChatCommand(session, chatId);
            await commander.Call(command);
            await AssertUserJoined(tester.AppServices, session, chatId, user);

            var leaveCommand = new IChats.LeaveChatCommand(session, chatId);
            await commander.Call(leaveCommand);

            var permissions = await chats.GetRules(session, chatId, default);
            permissions.CanRead.Should().Be(isPublicChat);
            permissions.CanWrite.Should().BeFalse();

            var chat = await chats.Get(session, chatId, default);
            if (isPublicChat)
                chat.Should().NotBeNull();
            else
                chat.Should().BeNull();

            await AssertUserNotJoined(tester.AppServices, session, chatId, user);

            // re-join again
            if (!isPublicChat) {
                var joinCommand = new IInvites.UseCommand(session, inviteId);
                await commander.Call(joinCommand);
                await WaitInviteActivationCompleted();
            }
            await commander.Call(command, default);
            await AssertUserJoined(tester.AppServices, session, chatId, user);
        }
    }

    private static async Task WaitInviteActivationCompleted()
        // Wait till subsequent commands that set session options are executed.
        // TODO(AY): provide a durable way to wait for subsequent commands completion.
        => await Task.Delay(1000);

    private static async Task AssertUserJoined(IServiceProvider services, Session session, Symbol chatId, User user)
    {
        var chats = services.GetRequiredService<IChats>();

        var permissions = await chats.GetRules(session, chatId, default);
        permissions.CanReadWrite.Should().BeTrue();

        var chat = await chats.Get(session, chatId, default);
        chat.Should().NotBeNull();
        var chatAuthors = services.GetRequiredService<IChatAuthors>();
        var author = await chatAuthors.GetOwnAuthor(session, chatId, default);
        author.Should().NotBeNull();
        author!.UserId.Should().Be(user.Id);
        author.HasLeft.Should().BeFalse();

        var ownChatIds = await chatAuthors.ListOwnChatIds(session, default);
        ownChatIds.Should().Contain(chatId);

        var userIds = await chatAuthors.ListUserIds(session, chatId, default);
        userIds.Should().Contain(user.Id);
        var authorIds = await chatAuthors.ListAuthorIds(session, chatId, default);
        authorIds.Should().Contain(author.Id);
    }

    private static async Task AssertUserNotJoined(IServiceProvider services, Session session, Symbol chatId, User user)
    {
        var chatAuthors = services.GetRequiredService<IChatAuthors>();
        var author = await chatAuthors.GetOwnAuthor(session, chatId, default);
        author!.HasLeft.Should().BeTrue();

        var ownChatIds = await chatAuthors.ListOwnChatIds(session, default);
        ownChatIds.Should().NotContain(chatId);

        var userIds = await chatAuthors.ListUserIds(session, chatId, default);
        userIds.Should().NotContain(user.Id);
        var authorIds = await chatAuthors.ListAuthorIds(session, chatId, default);
        authorIds.Should().NotContain(author.Id);
    }
}
