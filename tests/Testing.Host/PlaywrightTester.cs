using ActualChat.App.Server;
using Microsoft.Playwright;

namespace ActualChat.Testing.Host;

public sealed class PlaywrightTester : WebClientTester
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightTester(AppHost appHost, IServiceProvider? clientServices = null)
        : base(appHost, clientServices)
    { }

    public override async ValueTask DisposeAsync()
    {
        _playwright?.Dispose();
        await base.DisposeAsync();
    }

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
            BaseURL = UrlMapper.BaseUri.ToString(),
            BypassCSP = true,
        });
        await context.AddCookiesAsync(new[] {
            new Cookie() {
                Name = Constants.Session.CookieName,
                Value = Session.Id,
                Domain = "localhost",
                Path = "/",
                HttpOnly = true,
                Secure = false,
            }
        });
        return context;
    }

    public async Task<(IPage page, IResponse? response)> NewPage(string relativeUri = "")
    {
        var context = await NewContext();
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(UrlMapper.ToAbsolute(relativeUri).ToString());
        return (page, response);
    }
}
