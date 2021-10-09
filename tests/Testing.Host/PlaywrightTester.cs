using ActualChat.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace ActualChat.Testing.Host
{
    public class PlaywrightTester : IWebTester
    {
        private IPlaywright? _playwright;
        private IBrowser? _browser;

        public AppHost AppHost { get; }
        public IServiceProvider AppServices => AppHost.Services;
        public UriMapper UriMapper => AppServices.UriMapper();
        public IServerSideAuthService Auth => AppServices.GetRequiredService<IServerSideAuthService>();
        public Session Session { get; }

        public PlaywrightTester(AppHost appHost)
        {
            AppHost = appHost;
            var sessionFactory = AppServices.GetRequiredService<ISessionFactory>();
            Session = sessionFactory.CreateSession();
        }

        public void Dispose()
            => _playwright?.Dispose();

        public async ValueTask<IPlaywright> GetPlaywright()
            => _playwright ??= await Playwright.CreateAsync();

        public async ValueTask<IBrowser> GetBrowser()
        {
            if (_browser != null)
                return _browser;
            var launchOptions = new BrowserTypeLaunchOptions {
                Headless = false,
            };
            var playwright = await GetPlaywright();
            return _browser = await playwright.Chromium.LaunchAsync(launchOptions);
        }

        public async Task<IBrowserContext> NewContext()
        {
            var browser = await GetBrowser();
            var context = await browser.NewContextAsync(new () {
                BaseURL = UriMapper.BaseUri.ToString(),
                BypassCSP = true,
            });
            await context.AddCookiesAsync(new [] {
                new Cookie() {
                    Name = "FusionAuth.SessionId",
                    Value = Session.Id,
                    Domain = "localhost",
                    Path = "/",
                    HttpOnly = true,
                    Secure = false,
                }
            });
            return context;
        }

        public async Task<IPage> NewPage(string relativeUri = "")
        {
            var context = await NewContext();
            var page = await context.NewPageAsync();
            await page.GotoAsync(UriMapper.ToAbsolute(relativeUri).ToString());
            return page;
        }
    }
}
