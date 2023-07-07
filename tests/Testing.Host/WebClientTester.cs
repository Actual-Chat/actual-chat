using ActualChat.App.Wasm;
using ActualChat.App.Server;
using ActualChat.Users;
using Microsoft.Extensions.Configuration;

namespace ActualChat.Testing.Host;

public interface IWebTester : IDisposable, IAsyncDisposable
{
    AppHost AppHost { get; }
    IServiceProvider AppServices { get; }
    ICommander Commander { get; }
    IAuth Auth { get; }
    IAuthBackend AuthBackend { get; }
    Session Session { get; }
    UrlMapper UrlMapper { get; }
}

public interface IWebClientTester : IWebTester
{
    IServiceProvider ClientServices { get; }
    ICommander ClientCommander { get; }
    IAuth ClientAuth { get; }
}

public class WebClientTester : IWebClientTester
{
    private readonly bool _mustDisposeClientServices;
    private readonly Lazy<IServiceProvider> _clientServicesLazy;

    public AppHost AppHost { get; }
    public IServiceProvider AppServices => AppHost.Services;
    public ICommander Commander => AppServices.Commander();
    public IAuth Auth => AppServices.GetRequiredService<IAuth>();
    public IAuthBackend AuthBackend => AppServices.GetRequiredService<IAuthBackend>();
    public Session Session { get; }
    public UrlMapper UrlMapper => AppServices.UrlMapper();

    public IServiceProvider ClientServices => _clientServicesLazy.Value;
    public ICommander ClientCommander => ClientServices.Commander();
    public IAuth ClientAuth => ClientServices.GetRequiredService<IAuth>();

    public WebClientTester(AppHost appHost, IServiceProvider? clientServices = null)
    {
        AppHost = appHost;
        Session = Session.New();
        var sessionInfo = Commander.Call(new AuthBackend_SetupSession(Session)).Result;
        sessionInfo.GetGuestId().IsGuest.Should().BeTrue();
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
        var output = AppHost.Services.GetRequiredService<ITestOutputHelper>();
        var services = new ServiceCollection();
        var configuration = AppServices.GetRequiredService<IConfiguration>();
        Program.ConfigureServices(services, configuration, UrlMapper.BaseUrl);
        services.ConfigureLogging(output); // Override logging

        var serviceProvider = services.BuildServiceProvider();
        serviceProvider.HostedServices().Start().Wait();
        return serviceProvider;
    }
}
