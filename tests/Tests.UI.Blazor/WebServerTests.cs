using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;
using NUnit.Framework;
using Assert = Xunit.Assert;

namespace ActualChat.Tests.UI.Blazor
{
    public class WebServerTests : IClassFixture<WebServerFixture>
    {
        private readonly WebServerFixture _fixture;

        public WebServerTests(WebServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Test]
        public async Task MessageWordExists()
        {
            var serverFixture = new WebServerFixture();
            await serverFixture.InitializeAsync();
            var page = await _fixture.Browser?.NewPageAsync();
            await page.GotoAsync($"{_fixture.BaseUrl}chat/the-actual-one");
            var tableHeads = await page.QuerySelectorAllAsync("th");
            var thUser = tableHeads[0];
            var thMessage = tableHeads[1];

            var userCell = await thUser.TextContentAsync();
            var messageCell = await thMessage.TextContentAsync();
            // Assert.AreEqual("Message", messageCell);
            // Assert.AreEqual("User", userCell);
        }
        
        
    }
}
