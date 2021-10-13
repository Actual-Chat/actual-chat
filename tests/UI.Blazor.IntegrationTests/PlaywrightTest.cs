using ActualChat.Testing.Host;
using Microsoft.Playwright;

namespace ActualChat.UI.Blazor.IntegrationTests;

public class PlaywrightTest : AppHostTestBase
{
    public PlaywrightTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task CloseBrowserTest()
    {
        using var appHost = await TestHostFactory.NewAppHost();
        using var tester = appHost.NewPlaywrightTester();
        var browser = await tester.NewContext();
        await browser.CloseAsync();
    }

    [Fact]
    public async Task AddMessageTest()
    {
        const float timeout = 20_000f;
        using var appHost = await TestHostFactory.NewAppHost().ConfigureAwait(false);
        using var tester = appHost.NewPlaywrightTester();
        var user = await tester.SignIn(new User("", "it-works")).ConfigureAwait(false);
        var page = await tester.NewPage("chat/the-actual-one").ConfigureAwait(false);
        await page.WaitForLoadStateAsync(LoadState.Load,
            new PageWaitForLoadStateOptions() { Timeout = timeout }).ConfigureAwait(false);
        // TODO: wait for server-side blazor loading, something like page.WaitForWebSocketAsync

        await Task.Delay(2000).ConfigureAwait(false);

        var chatPage = await page.QuerySelectorAsync(".chat-page").ConfigureAwait(false);
        chatPage.Should().NotBeNull();
        var input = await page.QuerySelectorAsync("[role='textbox']").ConfigureAwait(false);
        input.Should().NotBeNull();
        var button = await page.QuerySelectorAsync("button.message-submit").ConfigureAwait(false);
        button.Should().NotBeNull();

        var messages = await page.QuerySelectorAllAsync(".chat-page .content").ConfigureAwait(false);
        var lastMessage = await messages.Last().TextContentAsync().ConfigureAwait(false);
        lastMessage.Should().NotBe("Test-123");

        await input!.TypeAsync("Test-123").ConfigureAwait(false);
        await button!.ClickAsync().ConfigureAwait(false);

        // TODO: wait for network request or websocket event
        await page.WaitForTimeoutAsync(3500f).ConfigureAwait(false);

        messages = await page.QuerySelectorAllAsync(".chat-page .content").ConfigureAwait(false);
        lastMessage = await messages.Last().TextContentAsync().ConfigureAwait(false);
        lastMessage.Should().Be("Test-123");
    }

    [Fact]
    public async Task ChatPageTest()
    {
        using var appHost = await TestHostFactory.NewAppHost();
        using var tester = appHost.NewPlaywrightTester();
        var user = await tester.SignIn(new User("", "ChatPageTester"));
        var page = await tester.NewPage("chat/the-actual-one");
        await Task.Delay(1000);
        user.Id.Value.Should().NotBeNullOrEmpty();
        user.Name.Should().Be("ChatPageTester");
        var messages = await page.QuerySelectorAllAsync(".chat-page .content");
        messages.Count.Should().BeGreaterThan(0);
        await Task.Delay(200);
    }
}
