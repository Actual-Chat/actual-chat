using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class AuthorRenamedContactTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private const string BobRenamed = "B YC";

    [Fact]
    public async Task AuthorShouldBeNamedTheWayTheViewerRenamedThem()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var alice = await tester.SignInAsUniqueAlice();
        var (chatId, inviteId) = await tester.CreateChat(false);
        var bob = await tester.SignInAsUniqueBob();
        var bobAuthor = await tester.JoinChat(chatId, inviteId);
        await tester.SignIn(alice);
        await tester.RenamePeerContact(alice, bob, BobRenamed);

        // act
        var author = await tester.GetAuthor(bobAuthor.Id).Require();

        // assert
        author.Avatar.Name.Should().Be(BobRenamed);
    }

    [Fact]
    public async Task RenameShouldNotUnmaskAnAnonymousAuthor()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var alice = await tester.SignInAsUniqueAlice();
        var (chatId, inviteId) = await tester.CreateChat(c => c with { AllowAnonymousAuthors = true });
        var bob = await tester.SignInAsUniqueBob();
        var anonymousAvatar = await tester.CreateAnonymousAvatar();
        var bobAuthor = await tester.JoinChat(chatId, inviteId, joinAnonymously: true, avatarId: anonymousAvatar.Id);
        await tester.SignIn(alice);
        await tester.RenamePeerContact(alice, bob, BobRenamed);

        // act
        var author = await tester.GetAuthor(bobAuthor.Id).Require();

        // assert
        author.IsAnonymous.Should().BeTrue();
        author.Avatar.Name.Should().Be(anonymousAvatar.Name, "a rename would tell Alice who's behind the avatar");
    }
}
