using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ActualChat.Testing;
using ActualChat.Users;
using ActualChat.Users.Client;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using NUnit.Framework;
using Stl.Fusion.Authentication;

namespace ActualChat.Tests.UI.Blazor
{
    public class PlaywrightTests
    {
        private enum TestingBrowser
        {
            Chromium,
            Firefox,
            Webkit
        }

        async Task<IBrowser> Browser(TestingBrowser testBrowser = TestingBrowser.Chromium)
        {
            var playwright = await Playwright.CreateAsync();
            IBrowser? browser;
            switch (testBrowser) {
                case TestingBrowser.Firefox:
                    browser = await playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions { Headless = false });
                    break;
                case TestingBrowser.Webkit:
                    browser = await playwright.Webkit.LaunchAsync(new BrowserTypeLaunchOptions { Headless = false });
                    break;
                default:
                    browser = await playwright.Chromium.LaunchAsync();
                    break;
            }
            return browser;
        }

        [Test]
        public async Task close_Browser()
        {
            var browser = await Browser();
            await browser.CloseAsync();
        }

        private async Task<IPage> Page()
        {
            var browser = await Browser();
            var page = await browser.NewPageAsync();
            return page;
        }

        [Test]
        public async Task close_Page()
        {
            var page = await Page();
            await page.CloseAsync();
        }
        
        [Test]
        public async Task WordsExistsOnPage()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            using var tester = appHost.NewBlazorTester();
            // var sessionFactory = services.GetRequiredService<ISessionFactory>();
            // var session = tester.Session;
            
            var user = new User("", "iqmulator1");
            await tester.SignIn(user);
            var sessionId = tester.Session.Id;
            
            var cookies = new List<Cookie>();
            var cookie = new Cookie {
                Name = "FusionAuth.SessionId",
                Value = sessionId,
                Domain = "localhost",
                Path = "/",
                HttpOnly = true,
                Secure = true,
            };
            cookies.Add(cookie);

            var baseUrl = appHost.ServerUrls;
            var browser = await Browser();
            var context = await browser.NewContextAsync();
            await context.AddCookiesAsync(cookies);
            
            var currentCookie = await context.CookiesAsync();
            
            var page = await Page();
            await page.GotoAsync($"{baseUrl}chat/the-actual-one");

            var content = await page.ContentAsync();
            user.IsAuthenticated.Should().Be(true);
        }
    }
}
