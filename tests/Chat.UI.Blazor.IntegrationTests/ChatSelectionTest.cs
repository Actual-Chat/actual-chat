using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class ChatSelectionTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task IsSelectedFollowsEverySwitchEvenWhenSwitchesComeBackToBack()
    {
        // arrange - each chat gets IsSelected cached while selected, the way the chat list does
        await Tester.SignInAsUniqueBob();
        var (chatA, _) = await Tester.CreateAndGetChat(false, "selection-a");
        var (chatB, _) = await Tester.CreateAndGetChat(false, "selection-b");
        var (chatC, _) = await Tester.CreateAndGetChat(false, "selection-c");
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        chatUI.SelectChatOnNavigation(chatA.Id);
        (await chatUI.IsSelected(chatA.Id)).Should().BeTrue();

        // act - two switches with no tick in between, as a deep link right after a restart produces
        chatUI.SelectChatOnNavigation(chatB.Id);
        (await chatUI.IsSelected(chatB.Id)).Should().BeTrue();
        chatUI.SelectChatOnNavigation(chatC.Id);

        // assert
        (await chatUI.IsSelected(chatA.Id)).Should().BeFalse();
        (await chatUI.IsSelected(chatB.Id)).Should().BeFalse("the skipped chat must not stay highlighted");
        (await chatUI.IsSelected(chatC.Id)).Should().BeTrue();
    }
}
