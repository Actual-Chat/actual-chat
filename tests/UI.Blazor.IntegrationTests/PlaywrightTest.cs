using System.Text.RegularExpressions;
using ActualChat.Testing.Host;

namespace ActualChat.UI.Blazor.IntegrationTests
{
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
            using var appHost = await TestHostFactory.NewAppHost();
            using var tester = appHost.NewPlaywrightTester();
            var user = await tester.SignIn(new User("", "it-fucking-works"));
            var page = await tester.NewPage("chat/the-actual-one");
            await Task.Delay(1000);

            var chatPage = await page.QuerySelectorAsync(".chat-page");
            chatPage.Should().NotBeNull();
            var input = await page.QuerySelectorAsync("[role='textbox']");
            input.Should().NotBeNull();
            var button = await page.QuerySelectorAsync("button >> nth=-1");
            button.Should().NotBeNull();

            var messages = await page.QuerySelectorAllAsync(".chat-page .content");
            (await messages.Last().TextContentAsync()).Should().NotBe("Test-123");

            await input!.TypeAsync("Test-123");
            await button!.ClickAsync();
            await Task.Delay(1000);

            messages = await page.QuerySelectorAllAsync(".chat-page .content");
            (await messages.Last().TextContentAsync()).Should().Be("Test-123");
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
}
