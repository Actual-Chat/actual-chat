using ActualChat.Testing.Host;
using Microsoft.Playwright;

namespace ActualChat.UI.Blazor.IntegrationTests;

public class PlaywrightTest : AppHostTestBase
{
    public PlaywrightTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task CloseBrowserTest()
    {
        using var appHost = await NewAppHost();
        using var tester = appHost.NewPlaywrightTester();
        var browser = await tester.NewContext();
        await browser.CloseAsync();
    }

    [Fact]
    public async Task AddMessageTest()
    {
        const float timeout = 20_000f;
        using var appHost = await NewAppHost().ConfigureAwait(false);
        using var tester = appHost.NewPlaywrightTester();
        var user = await tester.SignIn(new User("", "it-works")).ConfigureAwait(false);
        var (page, _) = await tester.NewPage("chat/the-actual-one").ConfigureAwait(false);
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

        var messages = await GetMessages(page).ConfigureAwait(false);
        var lastMessage = await GetLastMessage(messages).ConfigureAwait(false);
        lastMessage.Should().NotBe("Test-123");

        await input!.TypeAsync("Test-123").ConfigureAwait(false);
        await button!.ClickAsync().ConfigureAwait(false);

        var count = messages.Count;
        messages = await WaitNewMessages(TimeSpan.FromSeconds(5), page, count).ConfigureAwait(false);
        lastMessage = await GetLastMessage(messages).ConfigureAwait(false);
        lastMessage.Should().Be("Test-123");

        static async Task<IReadOnlyList<IElementHandle>> WaitNewMessages(TimeSpan timeout, IPage page, int oldMessagesCount)
        {
            var stopTime = DateTime.Now + timeout;
            var newMessages = await GetMessages(page).ConfigureAwait(false);
            while (newMessages.Count == oldMessagesCount) {
                await Task.Delay(500).ConfigureAwait(false);
                newMessages = await GetMessages(page).ConfigureAwait(false);
                if (DateTime.Now >= stopTime) {
                    throw new TimeoutException($"Chat state has not changed in {timeout.TotalSeconds} seconds.");
                }
            }
            return newMessages;
        }

        static async Task<IReadOnlyList<IElementHandle>> GetMessages(IPage page)
            => await page.QuerySelectorAllAsync(".chat-page .content");

        static async Task<string?> GetLastMessage(IEnumerable<IElementHandle> messages)
            => await messages.Last().TextContentAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task ChatPageTest()
    {
        using var appHost = await NewAppHost();
        using var tester = appHost.NewPlaywrightTester();
        var user = await tester.SignIn(new User("", "ChatPageTester"));
        var (page, _) = await tester.NewPage("chat/the-actual-one");
        await Task.Delay(1000);
        user.Id.Value.Should().NotBeNullOrEmpty();
        user.Name.Should().Be("ChatPageTester");
        var messages = await page.QuerySelectorAllAsync(".chat-page .content");
        messages.Count.Should().BeGreaterThan(0);
        await Task.Delay(200);
    }
}
