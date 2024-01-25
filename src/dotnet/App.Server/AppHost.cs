using ActualChat.Hosting;
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

    public virtual async Task InvokeDbInitializers(CancellationToken cancellationToken = default)
    {
        if (Services.HostInfo().IsTested) {
            // Just to speed up tests
            await InvokeDbInitializersUnsafe(cancellationToken).ConfigureAwait(false);
            return;
        }

        var meshLocks = Services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix(nameof(AppHost));
        var lockHolder = await meshLocks.Lock(nameof(InvokeDbInitializers), "", cancellationToken).ConfigureAwait(false);
        await using var _ = lockHolder.ConfigureAwait(false);
        var lockCts = cancellationToken.LinkWith(lockHolder.StopToken);

        await InvokeDbInitializersUnsafe(lockCts.Token).ConfigureAwait(false);
    }

    public virtual async Task InvokeDbInitializersUnsafe(CancellationToken cancellationToken = default)
    {
        // InitializeSchema
        await InvokeDbInitializers(
            nameof(IDbInitializer.InitializeSchema),
            (x, ct) => x.InitializeSchema(ct),
            cancellationToken
            ).ConfigureAwait(false);

        // InitializeData
        await InvokeDbInitializers(
            nameof(IDbInitializer.InitializeData),
            (x, ct) => x.InitializeData(ct),
            cancellationToken
            ).ConfigureAwait(false);

        // RepairData
        await InvokeDbInitializers(
            nameof(IDbInitializer.RepairData),
            x => x.ShouldRepairData,
            (x, ct) => x.RepairData(ct),
            cancellationToken
            ).ConfigureAwait(false);

        // VerifyData
        await InvokeDbInitializers(
            nameof(IDbInitializer.VerifyData),
            x => x.ShouldVerifyData,
            (x, ct) => x.VerifyData(ct),
            cancellationToken
            ).ConfigureAwait(false);
    }

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

    private Task InvokeDbInitializers(
        string name,
        Func<IDbInitializer, CancellationToken, Task> invoker,
        CancellationToken cancellationToken)
        => InvokeDbInitializers(name, _ => true, invoker, cancellationToken);

    private async Task InvokeDbInitializers(
        string name,
        Func<IDbInitializer, bool> mustInvokePredicate,
        Func<IDbInitializer, CancellationToken, Task> invoker,
        CancellationToken cancellationToken)
    {
        var log = Host.Services.LogFor(GetType());
        var runningTaskSources = Host.Services.GetServices<IDbInitializer>()
            .ToDictionary(x => x, _ => TaskCompletionSourceExt.New<bool>());
        var runningTasks = runningTaskSources
            .ToDictionary(kv => kv.Key, kv => (Task)kv.Value.Task);
        foreach (var (dbInitializer, _) in runningTasks)
            dbInitializer.RunningTasks = runningTasks;
        var tasks = runningTaskSources
            .Select(kv => mustInvokePredicate.Invoke(kv.Key) ? InvokeOne(kv.Key, kv.Value) : Task.CompletedTask)
            .ToArray();

        try {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally {
            foreach (var (dbInitializer, _) in runningTasks)
                dbInitializer.RunningTasks = null!;
        }
        return;

        async Task InvokeOne(IDbInitializer dbInitializer, TaskCompletionSource<bool> initializedSource)
        {
            using var _ = dbInitializer.Activate();
            var dbInitializerName = $"{dbInitializer.GetType().GetName()}.{name}";
            try {
                using var _1 = Tracer.Default.Region(dbInitializerName);
                log.LogInformation("{DbInitializer} started", dbInitializerName);
                var task = invoker.Invoke(dbInitializer, cancellationToken);
                if (task.IsCompletedSuccessfully)
                    log.LogInformation("{DbInitializer} completed synchronously (skipped?)", dbInitializerName);
                else {
                    await task.ConfigureAwait(false);
                    log.LogInformation("{DbInitializer} completed", dbInitializerName);
                }
                initializedSource.TrySetResult(default);
            }
            catch (OperationCanceledException) {
                initializedSource.TrySetCanceled(cancellationToken);
                throw;
            }
            catch (Exception e) {
                log.LogError(e, "{DbInitializer} failed", dbInitializerName);
                initializedSource.TrySetException(e);
                throw;
            }
        }
    }
}
