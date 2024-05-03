using ActualChat.App.Server;
using ActualChat.Chat;
using ActualChat.Hosting;
using ActualChat.UI;
using ActualChat.UI.Blazor.App;
using ActualChat.Users;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Testing.Host;

public interface IWebTester : IDisposable, IAsyncDisposable
{
    AppHost AppHost { get; }
    IServiceProvider AppServices { get; }
    ICommander Commander { get; }
    IAuth Auth { get; }
    IChats Chats { get; }
    IPlaces Places { get; }
    IAuthBackend AuthBackend { get; }
    Session Session { get; }
    UrlMapper UrlMapper { get; }
    ITestOutputHelper Out { get; }
}

public interface IWebClientTester : IWebTester
{
    IServiceProvider ClientServices { get; }
    ICommander ClientCommander { get; }
    IAuth ClientAuth { get; }
}

public class WebClientTester : IWebClientTester
{
    private readonly Lazy<IServiceProvider> _clientServicesLazy;

    public AppHost AppHost { get; }
    public IServiceProvider AppServices => AppHost.Services;
    public ICommander Commander => AppServices.Commander();
    public IAuth Auth => AppServices.GetRequiredService<IAuth>();
    public IChats Chats => AppServices.GetRequiredService<IChats>();
    public IPlaces Places => AppServices.GetRequiredService<IPlaces>();
    public IAuthBackend AuthBackend => AppServices.GetRequiredService<IAuthBackend>();
    public Session Session { get; }
    public UrlMapper UrlMapper => AppServices.UrlMapper();
    public ITestOutputHelper Out { get; }

    public IServiceProvider ClientServices => _clientServicesLazy.Value;
    public ICommander ClientCommander => ClientServices.Commander();
    public IAuth ClientAuth => ClientServices.GetRequiredService<IAuth>();

    public WebClientTester(
        AppHost appHost,
        ITestOutputHelper @out,
        Action<IServiceCollection>? configureClientServices = null)
    {
        AppHost = appHost;
        Out = @out;
        Session = Session.New();
        var sessionInfo = Commander.Call(new AuthBackend_SetupSession(Session)).Result;
        sessionInfo.GetGuestId().IsGuest.Should().BeTrue();
        _clientServicesLazy = new Lazy<IServiceProvider>(() => CreateClientServices(@out, configureClientServices));
    }

    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
        DisposeAsync().AsTask().Wait();
    }

    public virtual async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (!_clientServicesLazy.IsValueCreated)
            return;

        if (ClientServices is IAsyncDisposable ad)
            await ad.DisposeAsync();
        else if (ClientServices is IDisposable d)
            d.Dispose();
    }

    protected virtual IServiceProvider CreateClientServices(ITestOutputHelper output, Action<IServiceCollection>? configureClientServices)
    {
        var services = new ServiceCollection();
        var configuration = AppServices.Configuration();
        var hostInfo = ClientAppStartup.CreateHostInfo(configuration,
            Environments.Development,
            "Browser",
            HostKind.WasmApp,
            AppKind.Wasm,
            UrlMapper.BaseUrl,
            true);
        ClientAppStartup.ConfigureServices(services, hostInfo, Out.NewTracer());
        services.AddTestLogging(output); // Override logging
        services.AddSingleton<IDispatcherResolver>(c => new TestDispatcherResolver(c));
        configureClientServices?.Invoke(services);

        var serviceProvider = services.BuildServiceProvider();
        serviceProvider.HostedServices().Start().Wait();
        return serviceProvider;
    }
}

public class TestDispatcherResolver(IServiceProvider services) : IDispatcherResolver
{
    public IServiceProvider Services { get; } = services;
    public Task WhenReady { get; } = Task.CompletedTask;
    public Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
    public CancellationToken StopToken { get; } = CancellationToken.None;
}
