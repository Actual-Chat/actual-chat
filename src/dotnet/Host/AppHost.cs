using ActualChat.Hosting;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;

namespace ActualChat.Host;

public class AppHost : IDisposable
{
    public string ServerUrls { get; set; } = "http://localhost:7080;https://localhost:7081";
    public Action<IConfigurationBuilder>? HostConfigurationBuilder { get; set; }
    public Action<WebHostBuilderContext, IServiceCollection>? AppServicesBuilder { get; set; }
    public Action<IConfigurationBuilder>? AppConfigurationBuilder { get; set; }

    public IHost Host { get; protected set; } = null!;
    public IServiceProvider Services => Host.Services;

    public void Dispose()
        => Host?.Dispose();

    public virtual Task Build(CancellationToken cancellationToken = default)
    {
        var webBuilder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureHostConfiguration(ConfigureHostConfiguration)
            .ConfigureWebHostDefaults(builder => builder
                .UseDefaultServiceProvider((ctx, options) => {
                    if (ctx.HostingEnvironment.IsDevelopment()) {
                        options.ValidateScopes = true;
                        options.ValidateOnBuild = true;
                    }
                })
                .ConfigureAppConfiguration(ConfigureAppConfiguration)
                .UseStartup<Startup>()
                .ConfigureServices(ConfigureAppServices)
            );

        Host = webBuilder.Build();
        return Task.CompletedTask;
    }

    public virtual async Task Initialize(CancellationToken cancellationToken = default)
    {
        var log = Host.Services.LogFor(GetType());

        async Task InitializeOne(IDbInitializer dbInitializer, TaskSource<bool> taskSource)
        {
            try {
                log.LogInformation("{DbInitializer} started", dbInitializer.GetType().Name);
                await dbInitializer.Initialize(cancellationToken).ConfigureAwait(false);
                log.LogInformation("{DbInitializer} completed", dbInitializer.GetType().Name);
                taskSource.TrySetResult(default);
            }
            catch (OperationCanceledException) {
                taskSource.TrySetCanceled(cancellationToken);
                throw;
            }
            catch (Exception e) {
                log.LogError(e, "{DbInitializer} failed", dbInitializer.GetType());
                taskSource.TrySetException(e);
                throw;
            }
        }

        var initializeTaskSources = Host.Services.GetServices<IDbInitializer>()
            .ToDictionary(i => i, i => TaskSource.New<bool>(true));
        var initializeTasks = initializeTaskSources
            .ToDictionary(kv => kv.Key, kv => (Task)kv.Value.Task);
        foreach (var (dbInitializer, _) in initializeTasks)
            dbInitializer.InitializeTasks = initializeTasks;
        var tasks = initializeTaskSources
            .Select(kv => InitializeOne(kv.Key, kv.Value))
            .ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        // await Task.Delay(100, cancellationToken); // Just in case
    }

    public virtual Task Run(CancellationToken cancellationToken = default)
        => Host.RunAsync(cancellationToken);

    public virtual Task Start(CancellationToken cancellationToken = default)
        => Host.StartAsync(cancellationToken);

    public virtual Task Stop(CancellationToken cancellationToken = default)
        => Host.StopAsync(cancellationToken);

    // Protected methods

    protected virtual void ConfigureHostConfiguration(IConfigurationBuilder cfg)
    {
        // Looks like there is no better way to set _default_ URL
        cfg.Sources.Insert(0,
            new MemoryConfigurationSource {
                InitialData = new Dictionary<string, string>(StringComparer.Ordinal) {
                    { WebHostDefaults.ServerUrlsKey, ServerUrls },
                },
            });
        cfg.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
        HostConfigurationBuilder?.Invoke(cfg);
    }

    protected virtual void ConfigureAppConfiguration(IConfigurationBuilder appBuilder)
    {
        // disable FSW, because they eat a lot and can exhaust the handles available to epoll on linux containers
        var jsonProviders = appBuilder.Sources.OfType<JsonConfigurationSource>().Where(j => j.ReloadOnChange).ToArray();
        foreach (var item in jsonProviders) {
            appBuilder.Sources.Remove(item);
            appBuilder.AddJsonFile(item.Path, item.Optional, reloadOnChange: false);
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
