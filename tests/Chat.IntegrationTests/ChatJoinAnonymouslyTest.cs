using ActualChat.Testing.Host;
using ActualChat.Users;
using Stl.Fusion.Authentication.Commands;

namespace ActualChat.Chat.IntegrationTests;

public class ChatJoinAnonymouslyTest : AppHostTestBase
{
    public ChatJoinAnonymouslyTest(ITestOutputHelper @out) : base(@out) { }

    [Theory]
    // [InlineData(false)] TODO(DF): Guest user can not join to a chat with invite code, should we change this?
    [InlineData(true)]
    public async Task JoinWithGuestUser(bool isPublicChat)
    {
        using var appHost = await NewAppHost();

        var (chatId, inviteId) = await ChatOperations.CreateChat(appHost, isPublicChat);

        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        await tester.Commander.Call(new SetupSessionCommand(session)).ConfigureAwait(false);
        var accounts = tester.AppServices.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, default).ConfigureAwait(false);
        account.IsGuest.Should().BeTrue();

        var author = await ChatOperations.JoinChat(tester, chatId, inviteId);

        await ChatOperations.AssertJoined(tester, chatId);
        author.IsAnonymous.Should().BeTrue();

        var avatars = tester.AppServices.GetRequiredService<IAvatars>();
        var avatar = await avatars.GetOwn(session, author.AvatarId, default).ConfigureAwait(false);
        avatar.Should().NotBeNull();
        avatar!.IsAnonymous.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JoinWithSignedInUser(bool isPublicChat)
    {
        using var appHost = await NewAppHost();

        var (chatId, inviteId) = await ChatOperations.CreateChat(appHost, isPublicChat);

        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        await tester.SignIn(new User("", "Bob"));

        var accounts = tester.AppServices.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, default).ConfigureAwait(false);
        account.IsGuest.Should().BeFalse();

        var anonymous = await CreateAnonymousAvatar(tester).ConfigureAwait(false);

        var author = await ChatOperations
            .JoinChat(tester,
                chatId,
                inviteId,
                joinAnonymously: true,
                avatarId: anonymous.Id)
            .ConfigureAwait(false);

        await ChatOperations.AssertJoined(tester, chatId);
        author.IsAnonymous.Should().BeTrue();

        var avatars = tester.AppServices.GetRequiredService<IAvatars>();
        var avatar = await avatars.GetOwn(session, author.AvatarId, default).ConfigureAwait(false);
        avatar.Should().NotBeNull();
        avatar!.IsAnonymous.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JoinAnonymouslyForbidden(bool isGuest)
    {
        using var appHost = await NewAppHost();

        var (chatId, inviteId) = await ChatOperations.CreateChat(appHost,
            c => c with {
                IsPublic = true,
                AllowedAuthorKind = ChatAuthorKind.RegularOnly
            });

        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        if (isGuest)
            await tester.Commander.Call(new SetupSessionCommand(session)).ConfigureAwait(false);
        else
            await tester.SignIn(new User("", "Bob"));

        var accounts = tester.AppServices.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, default).ConfigureAwait(false);
        account.IsGuest.Should().Be(isGuest);

        if (isGuest)
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => {
                    _ = await ChatOperations.JoinChat(tester, chatId, inviteId);
                });
        else {
            var command = new IAvatars.ChangeCommand(session, Symbol.Empty, null, new Change<AvatarFull>() {
                Create = new AvatarFull() {
                    UserId = account.Id,
                    IsAnonymous = true,
                    Name = "Anonymous Bob",
                },
            });
            var anonymousAvatar = await tester.Commander.Call(command);
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => {
                    _ = await ChatOperations.JoinChat(tester,
                        chatId,
                        inviteId,
                        joinAnonymously: true,
                        avatarId: anonymousAvatar.Id);
                });
        }
    }

    private async Task<AvatarFull> CreateAnonymousAvatar(IWebTester tester)
    {
        var session = tester.Session;
        var accounts = tester.AppServices.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, default);
        var command = new IAvatars.ChangeCommand(session,
            Symbol.Empty,
            null,
            new Change<AvatarFull>() {
                Create = new AvatarFull() {
                    UserId = account.Id,
                    IsAnonymous = true,
                    Name = RandomNameGenerator.Default.Generate(),
                },
            });
        return await tester.Commander.Call(command).ConfigureAwait(false);
    }
}
