using ActualChat.App.Server.Initializers;
using ActualChat.Mesh;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;

namespace ActualChat.App.Server;

public class AppHost : IDisposable
{
    public static readonly string DefaultServerUrls = "http://localhost:7080";

    private volatile int _isDisposed;

    public string ServerUrls { get; set; } = DefaultServerUrls;
    public Action<IConfigurationBuilder>? HostConfigurationBuilder { get; set; }
    public Action<IConfigurationBuilder>? AppConfigurationBuilder { get; set; }
    public Action<WebHostBuilderContext, IServiceCollection>? AppServicesBuilder { get; set; }

    public IHost Host { get; protected set; } = null!;
    public IServiceProvider Services => Host.Services;

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0)
            return;

        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
            Host.DisposeSilently();
    }

    public virtual AppHost Build(bool configurationOnly = false)
    {
        var webBuilder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureHostConfiguration(ConfigureHostConfiguration)
            .ConfigureWebHostDefaults(host => {
                host
                    .UseDefaultServiceProvider((ctx, options) => {
                        if (ctx.HostingEnvironment.IsDevelopment()) {
                            options.ValidateScopes = true;
                            options.ValidateOnBuild = true;
                        }
                    })
                    .UseKestrel(ConfigureKestrel)
                    .ConfigureAppConfiguration(ConfigureAppConfiguration);
                if (!configurationOnly)
                    host
                        .UseStartup<Startup>()
                        .ConfigureServices(ConfigureAppServices)
                        .ConfigureServices(ValidateContainerRegistrations);
            });

        Host = webBuilder.Build();
        return this;
    }

    private void ValidateContainerRegistrations(WebHostBuilderContext webHostBuilderContext, IServiceCollection services)
    {
        if (!webHostBuilderContext.HostingEnvironment.IsDevelopment())
            return;

        var transientDisposables = services.Where(x => x.Lifetime == ServiceLifetime.Transient)
            .Select(x => AsDisposable(x.ImplementationType))
            .SkipNullItems()
            .Where(x => x.Namespace?.OrdinalIgnoreCaseStartsWith("Microsoft") != true)
            .ToList();
        if (transientDisposables.Count != 0) {
            var transientDisposablesString = string.Join("", transientDisposables.Select(x => $"{Environment.NewLine}- {x}"));
            throw new Exception($"Disposable transient services are not allowed: {transientDisposablesString}");
        }

        Type? AsDisposable(Type? type) => type?.IsAssignableTo(typeof(IDisposable)) == true
            || type?.IsAssignableTo(typeof(IAsyncDisposable)) == true ? type : null;
    }

    public virtual async Task InvokeInitializers(CancellationToken cancellationToken = default)
        => await InvokeInitializers([
                new ExecuteDbInitializers(Services),
                new ExecuteModuleInitializers(Services),
            ],
            cancellationToken
        )
        .ConfigureAwait(false);

    private async Task InvokeInitializers(IEnumerable<IAggregateInitializer> initializers, CancellationToken cancellationToken = default)
    {
#if DEBUG
        // See MeshLockBase.DefaultLockOptions - locks expire much longer in DEBUG
        var mustLock = false;
#else
        var hostInfo = Services.HostInfo();
        var mustLock = hostInfo.IsTested;
#endif

        var tasks = initializers.Select(initializer => mustLock
            ? InvokeInitializersProtected(initializer, cancellationToken)
            : InvokeInitializersUnsafe(initializer, cancellationToken));

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task InvokeInitializersProtected(IAggregateInitializer initializer, CancellationToken cancellationToken = default)
    {
        var meshLocks = Services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix(nameof(AppHost));
        var lockKey = initializer.GetType().Name;
        const string lockValue = nameof(InvokeInitializersProtected);
        var lockHolder = await meshLocks.Lock(lockKey, lockValue, cancellationToken).ConfigureAwait(false);
        await using var _ = lockHolder.ConfigureAwait(false);
        var lockCts = cancellationToken.LinkWith(lockHolder.StopToken);

        await InvokeInitializersUnsafe(initializer, lockCts.Token).ConfigureAwait(false);
    }

    private static async Task InvokeInitializersUnsafe(IAggregateInitializer initializer, CancellationToken cancellationToken)
        => await initializer
            .InvokeAll(cancellationToken)
            .ConfigureAwait(false);

    public virtual Task Run(CancellationToken cancellationToken = default)
        => Host.RunAsync(cancellationToken);

    public virtual Task Start(CancellationToken cancellationToken = default)
        => Host.StartAsync(cancellationToken);

    public virtual Task Stop(CancellationToken cancellationToken = default)
        => Host.StopAsync(cancellationToken);

    // Protected & private methods

    protected virtual void ConfigureHostConfiguration(IConfigurationBuilder cfg)
    {
        // Looks like there is no better way to set _default_ URL
        cfg.Sources.Insert(0,
            new MemoryConfigurationSource {
                InitialData = new Dictionary<string, string?>(StringComparer.Ordinal) {
                    { WebHostDefaults.ServerUrlsKey, ServerUrls },
                },
            });
        cfg.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
        HostConfigurationBuilder?.Invoke(cfg);
    }

    private void ConfigureKestrel(WebHostBuilderContext ctx, KestrelServerOptions options)
    { }

    protected virtual void ConfigureAppConfiguration(IConfigurationBuilder appBuilder)
    {
        // Disable FSW, because they eat a lot and can exhaust the handles available to epoll on linux containers
        var jsonProviders = appBuilder.Sources.OfType<JsonConfigurationSource>().Where(j => j.ReloadOnChange).ToArray();
        foreach (var item in jsonProviders) {
            appBuilder.Sources.Remove(item);
            appBuilder.AddJsonFile(item.Path!, item.Optional, reloadOnChange: false);
        }
        appBuilder.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
        appBuilder.AddEnvironmentVariables();

        AppConfigurationBuilder?.Invoke(appBuilder);
    }

    protected virtual void ConfigureAppServices(
        WebHostBuilderContext webHost,
        IServiceCollection services)
        => AppServicesBuilder?.Invoke(webHost, services);
}
