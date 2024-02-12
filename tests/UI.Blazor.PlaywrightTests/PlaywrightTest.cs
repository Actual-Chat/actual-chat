using ActualChat.Testing.Host;
using Microsoft.Playwright;

namespace ActualChat.UI.Blazor.PlaywrightTests;

[Collection(nameof(UIAutomationCollection)), Trait("Category", nameof(UIAutomationCollection))]
public class PlaywrightTest(AppHostFixture fixture, ITestOutputHelper @out)
    : AppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task CloseBrowserTest()
    {
        var appHost = Host;
        await using var tester = appHost.NewPlaywrightTester(Out);
        var browser = await tester.NewContext();
        await browser.CloseAsync();
    }

    [Fact]
    public async Task AddMessageTest()
    {
        const float timeout = 20_000f;
        var appHost = Host;
        await using var tester = appHost.NewPlaywrightTester(Out);
        var account = await tester.SignIn(new User("", "it-works"));
        var (page, _) = await tester.NewPage("chat/the-actual-one");
        await page.WaitForLoadStateAsync(LoadState.Load,
            new PageWaitForLoadStateOptions() { Timeout = timeout });
        // TODO: wait for server-side blazor loading, something like page.WaitForWebSocketAsync

        await Task.Delay(2000);

        var chatPage = await page.QuerySelectorAsync(".list-view-layout");
        chatPage.Should().NotBeNull();
        var input = await page.QuerySelectorAsync("[role='textbox']");
        input.Should().NotBeNull();
        var button = await page.QuerySelectorAsync("button.message-submit");
        button.Should().NotBeNull();

        var messages = await GetMessages(page);
        var lastMessage = await GetLastMessage(messages);
        lastMessage.Should().NotBe("Test-123");

        await input!.FillAsync("Test-123");
        await button!.ClickAsync();

        var count = messages.Count;
        messages = await WaitNewMessages(TimeSpan.FromSeconds(5), page, count);
        lastMessage = await GetLastMessage(messages);
        lastMessage.Should().Be("Test-123");

        static async Task<IReadOnlyList<IElementHandle>> WaitNewMessages(TimeSpan timeout, IPage page, int oldMessageCount)
        {
            var stopTime = DateTime.Now + timeout;
            var newMessages = await GetMessages(page);
            while (newMessages.Count == oldMessageCount) {
                await Task.Delay(500);
                newMessages = await GetMessages(page);
                if (DateTime.Now >= stopTime) {
                    throw new TimeoutException($"Chat state has not changed in {timeout.TotalSeconds} seconds.");
                }
            }
            return newMessages;
        }

        static async Task<IReadOnlyList<IElementHandle>> GetMessages(IPage page)
            => await page.QuerySelectorAllAsync(".list-view-layout .content");

        static async Task<string?> GetLastMessage(IEnumerable<IElementHandle> messages)
            => await messages.Last().TextContentAsync();
    }

    [Fact]
    public async Task ChatPageTest()
    {
        var appHost = Host;
        await using var tester = appHost.NewPlaywrightTester(Out);
        var account = await tester.SignIn(new User(Symbol.Empty, "ChatPageTester"));
        var (page, _) = await tester.NewPage("chat/the-actual-one");

        await Task.Delay(1000);
        account.Id.Value.Should().NotBeNullOrEmpty();
        account.User.Name.Should().Be("ChatPageTester");
        var messages = await page.QuerySelectorAllAsync(".list-view-layout .content");
        messages.Count.Should().BeGreaterThan(0);
        await Task.Delay(200);
    }
}
