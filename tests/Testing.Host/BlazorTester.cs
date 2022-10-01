using ActualChat.App.Server;
using Bunit;

namespace ActualChat.Testing.Host;

public class BlazorTester : TestContext, IWebTester
{
    private readonly IServiceScope _serviceScope;

    public AppHost AppHost { get; }
    public IServiceProvider AppServices => AppHost.Services;
    public IServiceProvider ScopedAppServices => _serviceScope!.ServiceProvider;
    public ICommander Commander => AppServices.Commander();
    public IAuth Auth => AppServices.GetRequiredService<IAuth>();
    public IAuthBackend AuthBackend => AppServices.GetRequiredService<IAuthBackend>();
    public Session Session { get; }
    public UrlMapper UrlMapper => AppServices.UrlMapper();

    public BlazorTester(AppHost appHost)
    {
        AppHost = appHost;
        _serviceScope = AppServices.CreateScope();
        Services.AddFallbackServiceProvider(ScopedAppServices);

        var sessionFactory = AppServices.GetRequiredService<ISessionFactory>();
        Session = sessionFactory.CreateSession();
        var sessionProvider = ScopedAppServices.GetRequiredService<ISessionProvider>();
        sessionProvider.Session = Session;

        Services.AddTransient(_ => ScopedAppServices.StateFactory());
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;
        _serviceScope.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose(true);
        return ValueTask.CompletedTask;
    }
}
