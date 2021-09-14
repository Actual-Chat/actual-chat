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
        public async Task HasMessageTableTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            using var tester = appHost.NewPlaywrightTester();
            var user = await tester.SignIn(new User("", "it-fucking-works"));
            var page = await tester.NewPage("chat/the-actual-one");
            await Task.Delay(200);
            var trs = await page.QuerySelectorAllAsync("table tr");
            trs.Count.Should().Be(1);

            var input = await page.QuerySelectorAsync("input[type='search']");
            await input!.TypeAsync("Test");
            await input.PressAsync("Enter");
            await Task.Delay(200);
            trs = await page.QuerySelectorAllAsync("table tr");
            trs.Count.Should().Be(2);
        }
    }
}
