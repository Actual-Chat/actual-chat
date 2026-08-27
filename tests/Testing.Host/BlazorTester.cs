using ActualChat.App.Server;
using ActualChat.Notifications;
using ActualChat.Search;
using ActualChat.UI;
using ActualChat.UI.Blazor.Services;
using ActualLab.Versioning;
using Bunit;

namespace ActualChat.Testing.Host;

public class BlazorTester : BunitContext, IWebTester
{
    private readonly IServiceScope _serviceScope;

    public AppHost AppHost { get; }
    public IServiceProvider AppServices => field ??= AppHost.Services;
    public IServiceProvider ScopedAppServices => _serviceScope.ServiceProvider;
    public ICommander Commander => field ??= AppServices.Commander();
    public IAccounts Accounts => field ??= AppServices.GetRequiredService<IAccounts>();
    public IAuthors Authors => field ??= AppServices.GetRequiredService<IAuthors>();
    public IAuthorsBackend AuthorsBackend => field ??= AppServices.GetRequiredService<IAuthorsBackend>();
    public IAccountsBackend AccountsBackend => field ??= AppServices.GetRequiredService<IAccountsBackend>();
    public IChats Chats => field ??= AppServices.GetRequiredService<IChats>();
    public IConversations Conversations => field ??= AppServices.GetRequiredService<IConversations>();
    public ITranslations Translations => field ??= AppServices.GetRequiredService<ITranslations>();
    public IPlaces Places => field ??= AppServices.GetRequiredService<IPlaces>();
    public ISearch Search => field ??= AppServices.GetRequiredService<ISearch>();
    public ISessionsBackend SessionsBackend => field ??= AppServices.GetRequiredService<ISessionsBackend>();
    public INotificationsBackend NotificationsBackend  => field ??= AppServices.GetRequiredService<INotificationsBackend>();
    public UserSettingsUI UserSettingsUI => field ??= ScopedAppServices.UserSettingsUI(Session);
    public Session Session { get; }
    public UrlMapper UrlMapper => field ??= AppServices.UrlMapper();
    public VersionGenerator<long> VersionGenerator => field ??= AppServices.VersionGenerator<long>();
    public ITestOutputHelper Out { get; }

    public BlazorTester(AppHost appHost, ITestOutputHelper @out)
    {
        AppHost = appHost;
        Out = @out;
        _serviceScope = AppServices.CreateScope();
        Services.AddFallbackServiceProvider(ScopedAppServices);

        Session = Session.New();
        var sessionResolver = ScopedAppServices.GetRequiredService<ISessionResolver>();
        sessionResolver.Session = Session;

        Services.AddTransient(_ => ScopedAppServices.StateFactory());
        InitializeBrowserInfo();
    }

    private void InitializeBrowserInfo()
    {
        // In the app BrowserInfo is initialized from JS, and there is no JS here - so its WhenReady
        // never completes on its own, and LanguageUI's missing-value factory, which waits for it to
        // learn the client languages, leaves LanguageUI.WhenReady pending forever.
        var browserInfo = ScopedAppServices.GetRequiredService<BrowserInfo>();
        browserInfo.OnInitialized(new IBrowserInfoBackend.InitResult(
            ScreenSizeText: nameof(ScreenSize.Unknown),
            WindowHeight: 0,
            IsVisible: true,
            IsHoverable: false,
            ThemeInfo: new IBrowserInfoBackend.ThemeInfo(null, nameof(Theme.Light), nameof(Theme.Light), ""),
            // No client languages, so LanguageUI falls back to Languages.Main
            UILanguageInfo: new IBrowserInfoBackend.UILanguageInfo(null, null, []),
            DefaultTheme: nameof(Theme.Light),
            UtcOffset: 0,
            TimeZone: "UTC",
            IsMobile: false,
            IsAndroid: false,
            IsIos: false,
            IsMacOS: false,
            IsChromium: false,
            IsEdge: false,
            IsWebKit: false,
            IsTouchCapable: false,
            CanVibrate: false,
            IsWasmReady: false,
            WindowId: ""));
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await base.DisposeAsyncCore();
        if (_serviceScope is IAsyncDisposable ad)
            await ad.DisposeSilentlyAsync();
        else
            _serviceScope.DisposeSilently();
    }
}
