using System.Linq;
using System.Threading.Tasks;
using ActualChat.Testing;
using FluentAssertions;
using Stl.Fusion.Authentication;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests.UI.Blazor
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
            await Task.Delay(200);

            var chatPage = await page.QuerySelectorAsync(".chat-page");
            chatPage.Should().NotBeNull();
            var input = await page.QuerySelectorAsync("input[type='search']");
            input.Should().NotBeNull();

            await input!.TypeAsync("Test-123");
            await input.PressAsync("Enter");
            await Task.Delay(200);

            var messages = await page.QuerySelectorAllAsync(".chat-page .message .content");
            messages.Count.Should().BeGreaterThan(0);
            (await messages.Last().TextContentAsync()).Should().Be("Test-123");
        }
    }
}
