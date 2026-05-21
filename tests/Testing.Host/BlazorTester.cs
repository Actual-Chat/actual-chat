using ActualChat.App.Server;
using ActualChat.Notifications;
using ActualChat.Search;
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
