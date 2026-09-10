using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public class AuthorUITest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task GetUserNameShouldReturnOwnAvatarNameInChatYouAreNotMemberOf()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(true, "author-ui-own-name-test");
        await Tester.SignInAsNew("Alice");
        var alice = await Tester.Accounts.GetOwn(Tester.Session, CancellationToken.None);
        alice.Avatar.Name.Should().NotBeEmpty();
        var authorUI = Tester.ScopedAppServices.GetRequiredService<AuthorUI>();

        // act
        var name = await authorUI.GetUserName(chat.Id, alice.Id, CancellationToken.None);

        // assert
        name.Should().Be(alice.Avatar.Name, "there is no rename of yourself, and your avatar name is what others see");
    }
}
