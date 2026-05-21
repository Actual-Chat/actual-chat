using ActualChat.App.Server;
using ActualChat.Chat;
using ActualChat.Hosting;
using ActualChat.Notifications;
using ActualChat.Search;
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
    IAccounts Accounts { get; }
    IAuthors Authors { get; }
    IAuthorsBackend AuthorsBackend { get; }
    IAccountsBackend AccountsBackend { get; }
    IChats Chats { get; }
    ITranslations Translations { get; }
    IPlaces Places { get; }
    ISearch Search { get; }
    ISessionsBackend SessionsBackend { get; }
    INotificationsBackend NotificationsBackend { get; }
    Session Session { get; }
    UrlMapper UrlMapper { get; }
    ITestOutputHelper Out { get; }
}

public interface IWebClientTester : IWebTester
{
    IServiceProvider ClientServices { get; }
    ICommander ClientCommander { get; }
}

public class WebClientTester : IWebClientTester
{
    private readonly Lazy<IServiceProvider> _clientServicesLazy;

    public AppHost AppHost { get; }
    public IServiceProvider AppServices => AppHost.Services;
    public ICommander Commander => field ??= AppServices.Commander();
    public IAuthors Authors => field ??= AppServices.GetRequiredService<IAuthors>();
    public IAuthorsBackend AuthorsBackend => field ??= AppServices.GetRequiredService<IAuthorsBackend>();
    public IAccounts Accounts => field ??= AppServices.GetRequiredService<IAccounts>();
    public IAccountsBackend AccountsBackend => field ??= AppServices.GetRequiredService<IAccountsBackend>();
    public IAvatars Avatars => field ??= AppServices.GetRequiredService<IAvatars>();
    public IChats Chats => field ??= AppServices.GetRequiredService<IChats>();
    public ITranslations Translations => field ??= AppServices.GetRequiredService<ITranslations>();
    public IPlaces Places => field ??= AppServices.GetRequiredService<IPlaces>();
    public ISearch Search => field ??= AppServices.GetRequiredService<ISearch>();
    public ISessionsBackend SessionsBackend => field ??= AppServices.GetRequiredService<ISessionsBackend>();
    public INotificationsBackend NotificationsBackend  => field ??= AppServices.GetRequiredService<INotificationsBackend>();
    public UrlMapper UrlMapper => field ??= AppServices.UrlMapper();
    public ITestOutputHelper Out { get; }
    public Session Session { get; }

    public IServiceProvider ClientServices => _clientServicesLazy.Value;
    public ICommander ClientCommander => field ??= ClientServices.Commander();

    public WebClientTester(
        AppHost appHost,
        ITestOutputHelper @out,
        Action<IServiceCollection>? configureClientServices = null)
    {
        AppHost = appHost;
        Out = @out;
        Session = Session.New();
        var sessionInfo = Commander.Call(new SessionsBackend_Upsert(Session)).GetAwaiter().GetResult();
        var guestId = sessionInfo.GetGuestId();
        guestId.Should().NotBeNull();
        guestId.IsGuest.Should().BeTrue();
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
        var hostInfo = ClientStartup.CreateHostInfo(configuration,
            Environments.Development,
            "Browser",
            HostKind.WasmApp,
            AppKind.Wasm,
            UrlMapper.BaseUrl,
            true);
        ClientStartup.ConfigureServices(services, hostInfo, null, Out.NewTracer());
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
    public Task WhenInitialized { get; } = Task.CompletedTask;
    public Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
    public CancellationToken StopToken { get; } = CancellationToken.None;
}
