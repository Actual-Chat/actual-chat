using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ChatJoinAnonymouslyTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task JoinWithGuestUser()
    {
        var appHost = AppHost;
        var tester = appHost.NewBlazorTester();

        // Guest user can not join to a chat with invite code, should we change this?
        await tester.SignInAsAlice();
        var (chatId, inviteId) = await tester.CreateChat(c => c with {
                IsPublic = true,
                AllowGuestAuthors = true,
            });
        await tester.SignOut();

        var session = tester.Session;
        await tester.Commander.Call(new AuthBackend_SetupSession(session));
        var accounts = tester.AppServices.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, default);
        account.IsGuest.Should().BeTrue();

        var author = await tester.JoinChat(chatId, inviteId);

        await tester.AssertJoined(chatId);
        author.IsAnonymous.Should().BeTrue();

        var avatars = tester.AppServices.GetRequiredService<IAvatars>();
        var avatar = await avatars.GetOwn(session, author.AvatarId, default);
        avatar.Should().NotBeNull();
        avatar!.IsAnonymous.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JoinAnonymouslyWithSignedInUser(bool isPublicChat)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();

        await tester.SignInAsAlice();
        var (chatId, inviteId) = await tester.CreateChat(c => c with {
                IsPublic = isPublicChat,
                AllowAnonymousAuthors = true,
            });
        await tester.SignOut();

        var session = tester.Session;
        await tester.SignInAsBob();

        var accounts = tester.AppServices.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, default);
        account.IsGuest.Should().BeFalse();

        var anonymous = await CreateAnonymousAvatar(tester);

        var author = await tester
            .JoinChat(chatId,
                inviteId,
                joinAnonymously: true,
                avatarId: anonymous.Id);

        await tester.AssertJoined(chatId);
        author.IsAnonymous.Should().BeTrue();

        var avatars = tester.AppServices.GetRequiredService<IAvatars>();
        var avatar = await avatars.GetOwn(session, author.AvatarId, default);
        avatar.Should().NotBeNull();
        avatar!.IsAnonymous.Should().BeTrue();
    }

    [Fact]
    public async Task JoinAsGuestShouldBeForbidden()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();

        await tester.SignInAsAlice();
        var (chatId, _) = await tester.CreateChat(true);
        await tester.SignOut();

        var session = tester.Session;
        await tester.Commander.Call(new AuthBackend_SetupSession(session));

        var accounts = tester.AppServices.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, default);
        account.IsGuest.Should().BeTrue();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => {
                _ = await tester.JoinChat(chatId, Symbol.Empty);
            });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JoinAnonymouslyShouldBeForbidden(bool isPublicChat)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();

        await tester.SignInAsAlice();
        var (chatId, inviteId) = await tester.CreateChat(isPublicChat);

        var session = tester.Session;
        await tester.SignInAsBob();

        var accounts = tester.AppServices.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, default);
        account.IsGuest.Should().BeFalse();

        var command = new Avatars_Change(session, Symbol.Empty, null, new Change<AvatarFull>() {
            Create = new AvatarFull(account.Id) {
                IsAnonymous = true,
                Name = "Anonymous Bob",
            },
        });
        var anonymousAvatar = await tester.Commander.Call(command);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => {
                _ = await tester.JoinChat(chatId,
                    inviteId,
                    joinAnonymously: true,
                    avatarId: anonymousAvatar.Id);
            });
    }

    private async Task<AvatarFull> CreateAnonymousAvatar(IWebTester tester)
    {
        var session = tester.Session;
        var accounts = tester.AppServices.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, default);
        var command = new Avatars_Change(session,
            Symbol.Empty,
            null,
            new Change<AvatarFull> {
                Create = new AvatarFull(account.Id) {
                    IsAnonymous = true,
                    Name = RandomNameGenerator.Default.Generate(),
                },
            });
        return await tester.Commander.Call(command).ConfigureAwait(false);
    }
}
