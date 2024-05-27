using ActualChat.App.Server;
using ActualChat.Chat;
using ActualChat.Search;
using ActualChat.Users;
using Bunit;

namespace ActualChat.Testing.Host;

public class BlazorTester : TestContext, IWebTester
{
    private readonly IServiceScope _serviceScope;

    public AppHost AppHost { get; }
    public IServiceProvider AppServices => AppHost.Services;
    public IServiceProvider ScopedAppServices => _serviceScope.ServiceProvider;
    public ICommander Commander => AppServices.Commander();
    public IAuth Auth => AppServices.GetRequiredService<IAuth>();
    public IAccounts Accounts => AppServices.GetRequiredService<IAccounts>();
    public IChats Chats => AppServices.GetRequiredService<IChats>();
    public IPlaces Places => AppServices.GetRequiredService<IPlaces>();
    public ISearch Search => AppServices.GetRequiredService<ISearch>();
    public ISearchBackend SearchBackend => AppServices.GetRequiredService<ISearchBackend>();
    public IAuthBackend AuthBackend => AppServices.GetRequiredService<IAuthBackend>();
    public Session Session { get; }
    public UrlMapper UrlMapper => AppServices.UrlMapper();
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
