using ActualChat.Host;
using ActualChat.UI.Blazor.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;

namespace ActualChat.Testing.Host;

public interface IWebTester : IDisposable
{
    public AppHost AppHost { get; }
    public IServiceProvider AppServices { get; }
    public UriMapper UriMapper { get; }
    public IAuth Auth { get; }
    public IAuthBackend AuthBackend { get; }
    public Session Session { get; }
}

public interface IWebClientTester : IWebTester
{
    public IServiceProvider ClientServices { get; }
    public IAuth ClientAuth { get; }
}

public class WebClientTester : IWebClientTester
{
    private readonly bool _mustDisposeClientServices;
    private readonly Lazy<IServiceProvider> _clientServicesLazy;

    public AppHost AppHost { get; }
    public IServiceProvider AppServices => AppHost.Services;
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
    {
        if (_mustDisposeClientServices && _clientServicesLazy.IsValueCreated && ClientServices is IDisposable d)
            d.Dispose();
        GC.SuppressFinalize(this);
    }

    protected virtual IServiceProvider CreateClientServices()
    {
        var services = new ServiceCollection();
        var configuration = AppServices.GetRequiredService<IConfiguration>();
        Program.ConfigureServices(services, configuration, UriMapper).Wait();

        var serviceProvider = services.BuildServiceProvider();
        serviceProvider.HostedServices().Start().Wait();
        return serviceProvider;
    }
}
