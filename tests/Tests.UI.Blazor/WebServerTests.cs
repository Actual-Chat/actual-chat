using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace ActualChat.Tests.UI.Blazor
{
    public class WebServerTests : IClassFixture<WebServerFixture>
    {
        private readonly WebServerFixture _fixture;

        public WebServerTests(WebServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task MessageWordExists()
        {
            var serverFixture = new WebServerFixture();
            await serverFixture.InitializeAsync();
            var page = await _fixture.Browser.NewPageAsync();
            await page.GotoAsync($"{_fixture.BaseUrl}chat/the-actual-one");
            var tableHeads = await page.QuerySelectorAllAsync("th");
            var thUser = tableHeads[0];
            var thMessage = tableHeads[1];

            var userCell = await thUser.TextContentAsync();
            var messageCell = await thMessage.TextContentAsync();
            Assert.Equal("Message", messageCell);
            Assert.Equal("User", userCell);

        }
    }
}
