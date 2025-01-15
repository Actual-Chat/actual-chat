using System.Diagnostics.CodeAnalysis;
using ActualChat.App.Server;
using ActualChat.Chat;
using ActualChat.Hosting;
using ActualChat.Notification;
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
    IAuth Auth { get; }
    IAccounts Accounts { get; }
    IAuthors Authors { get; }
    IAuthorsBackend AuthorsBackend { get; }
    IAccountsBackend AccountsBackend { get; }
    IChats Chats { get; }
    IPlaces Places { get; }
    ISearch Search { get; }
    IAuthBackend AuthBackend { get; }
    INotificationsBackend NotificationsBackend { get; }
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
    [field: AllowNull, MaybeNull]
    public ICommander Commander => field ??= AppServices.Commander();
    [field: AllowNull, MaybeNull]
    public IAuth Auth => field ??= AppServices.GetRequiredService<IAuth>();
    [field: AllowNull, MaybeNull]
    public IAuthors Authors => field ??= AppServices.GetRequiredService<IAuthors>();
    [field: AllowNull, MaybeNull]
    public IAuthorsBackend AuthorsBackend => field ??= AppServices.GetRequiredService<IAuthorsBackend>();
    [field: AllowNull, MaybeNull]
    public IAccounts Accounts => field ??= AppServices.GetRequiredService<IAccounts>();
    [field: AllowNull, MaybeNull]
    public IAccountsBackend AccountsBackend => field ??= AppServices.GetRequiredService<IAccountsBackend>();
    [field: AllowNull, MaybeNull]
    public IChats Chats => field ??= AppServices.GetRequiredService<IChats>();
    [field: AllowNull, MaybeNull]
    public IPlaces Places => field ??= AppServices.GetRequiredService<IPlaces>();
    [field: AllowNull, MaybeNull]
    public ISearch Search => field ??= AppServices.GetRequiredService<ISearch>();
    [field: AllowNull, MaybeNull]
    public IAuthBackend AuthBackend => field ??= AppServices.GetRequiredService<IAuthBackend>();
    [field: AllowNull, MaybeNull]
    public INotificationsBackend NotificationsBackend  => field ??= AppServices.GetRequiredService<INotificationsBackend>();
    [field: AllowNull, MaybeNull]
    public UrlMapper UrlMapper => field ??= AppServices.UrlMapper();
    public ITestOutputHelper Out { get; }
    public Session Session { get; }

    public IServiceProvider ClientServices => _clientServicesLazy.Value;
    [field: AllowNull, MaybeNull]
    public ICommander ClientCommander => field ??= ClientServices.Commander();
    [field: AllowNull, MaybeNull]
    public IAuth ClientAuth => field ??= ClientServices.GetRequiredService<IAuth>();

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
