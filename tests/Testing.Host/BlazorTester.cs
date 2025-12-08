using ActualChat.App.Server;
using ActualChat.Chat;
using ActualChat.Kvas;
using ActualChat.Notification;
using ActualChat.Search;
using ActualChat.Users;
using ActualLab.Versioning;
using Bunit;

namespace ActualChat.Testing.Host;

public class BlazorTester : TestContext, IWebTester
{
    private readonly IServiceScope _serviceScope;

    public AppHost AppHost { get; }
    public IServiceProvider AppServices => field ??= AppHost.Services;
    public IServiceProvider ScopedAppServices => _serviceScope.ServiceProvider;
    public ICommander Commander => field ??= AppServices.Commander();
    public IAuth Auth => field ??= AppServices.GetRequiredService<IAuth>();
    public IAccounts Accounts => field ??= AppServices.GetRequiredService<IAccounts>();
    public IAuthors Authors => field ??= AppServices.GetRequiredService<IAuthors>();
    public IAuthorsBackend AuthorsBackend => field ??= AppServices.GetRequiredService<IAuthorsBackend>();
    public IAccountsBackend AccountsBackend => field ??= AppServices.GetRequiredService<IAccountsBackend>();
    public IChats Chats => field ??= AppServices.GetRequiredService<IChats>();
    public ITranslations Translations => field ??= AppServices.GetRequiredService<ITranslations>();
    public IPlaces Places => field ??= AppServices.GetRequiredService<IPlaces>();
    public ISearch Search => field ??= AppServices.GetRequiredService<ISearch>();
    public IAuthBackend AuthBackend => field ??= AppServices.GetRequiredService<IAuthBackend>();
    public INotificationsBackend NotificationsBackend  => field ??= AppServices.GetRequiredService<INotificationsBackend>();
    public AccountSettings AccountSettings => field ??= ScopedAppServices.AccountSettings();
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

#pragma warning disable CA2215 // Ensure method calls base.Dispose(bool)
    protected override void Dispose(bool disposing)
#pragma warning restore CA2215
    {
        if (disposing)
            _serviceScope.DisposeSilently();
        // base.Dispose(disposing);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        Dispose(true);
        return default;
    }
}
