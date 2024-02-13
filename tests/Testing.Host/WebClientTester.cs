using ActualChat.App.Wasm;
using ActualChat.App.Server;
using ActualChat.Chat;
using ActualChat.UI;
using ActualChat.Users;
using Microsoft.AspNetCore.Components;
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

    public IServiceProvider ClientServices => _clientServicesLazy.Value;
    public ICommander ClientCommander => ClientServices.Commander();
    public IAuth ClientAuth => ClientServices.GetRequiredService<IAuth>();

    public WebClientTester(AppHost appHost, ITestOutputHelper output, Action<IServiceCollection>? configureClientServices = null)
    {
        AppHost = appHost;
        Session = Session.New();
        var sessionInfo = Commander.Call(new AuthBackend_SetupSession(Session)).Result;
        sessionInfo.GetGuestId().IsGuest.Should().BeTrue();
        _clientServicesLazy = new Lazy<IServiceProvider>(() => CreateClientServices(output, configureClientServices));
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
        var configuration = AppServices.GetRequiredService<IConfiguration>();
        Program.ConfigureServices(services, configuration, UrlMapper.BaseUrl, true);
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
