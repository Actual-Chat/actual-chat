using ActualChat.Host;
using ActualChat.UI.Blazor.Host;
using Microsoft.Extensions.Configuration;

namespace ActualChat.Testing.Host;

public interface IWebTester : IDisposable, IAsyncDisposable
{
    AppHost AppHost { get; }
    IServiceProvider AppServices { get; }
    ICommander Commander { get; }
    UriMapper UriMapper { get; }
    IAuth Auth { get; }
    IAuthBackend AuthBackend { get; }
    Session Session { get; }
}

public interface IWebClientTester : IWebTester
{
    IServiceProvider ClientServices { get; }
    IAuth ClientAuth { get; }
}

public class WebClientTester : IWebClientTester
{
    private readonly bool _mustDisposeClientServices;
    private readonly Lazy<IServiceProvider> _clientServicesLazy;

    public AppHost AppHost { get; }
    public IServiceProvider AppServices => AppHost.Services;
    public ICommander Commander => AppServices.Commander();
    public UriMapper UriMapper => AppServices.UriMapper();
    public IAuth Auth => AppServices.GetRequiredService<IAuth>();
    public IAuthBackend AuthBackend => AppServices.GetRequiredService<IAuthBackend>();
    public IServiceProvider ClientServices => _clientServicesLazy.Value;
    public IAuth ClientAuth => ClientServices.GetRequiredService<IAuth>();
    public Session Session { get; }

    public WebClientTester(AppHost appHost, IServiceProvider? clientServices = null)
    {
        AppHost = appHost;
        var sessionFactory = AppServices.GetRequiredService<ISessionFactory>();
        Session = sessionFactory.CreateSession();
        _mustDisposeClientServices = clientServices == null;
        _clientServicesLazy = new Lazy<IServiceProvider>(() => clientServices ?? CreateClientServices());
    }

    public virtual void Dispose()
        => DisposeAsync().AsTask().Wait();

    public virtual async ValueTask DisposeAsync()
    {
        if (!_mustDisposeClientServices || !_clientServicesLazy.IsValueCreated)
            return;

        if (ClientServices is IAsyncDisposable ad)
            await ad.DisposeAsync();
        else if (ClientServices is IDisposable d)
            d.Dispose();
    }

    protected virtual IServiceProvider CreateClientServices()
    {
        var services = new ServiceCollection();
        var configuration = AppServices.GetRequiredService<IConfiguration>();
        Program.ConfigureServices(services, configuration, UriMapper.BaseUri).Wait();

        var serviceProvider = services.BuildServiceProvider();
        serviceProvider.HostedServices().Start().Wait();
        return serviceProvider;
    }
}
