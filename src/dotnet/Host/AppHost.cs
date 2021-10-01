using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ActualChat.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stl.Async;

namespace ActualChat.Host
{
    public class AppHost : IDisposable
    {
        public string ServerUrls { get; set; } = "http://localhost:7080";
        public Action<IConfigurationBuilder>? HostConfigurationBuilder { get; set; }
        public Action<WebHostBuilderContext, IServiceCollection>? AppServicesBuilder { get; set; }

        public IHost Host { get; protected set; } = null!;
        public IServiceProvider Services => Host.Services;

        public void Dispose()
            => Host?.Dispose();

        public virtual Task Build(CancellationToken cancellationToken = default)
        {
            Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                .ConfigureHostConfiguration(ConfigureHostConfiguration)
                .ConfigureWebHostDefaults(builder => builder
                    .UseDefaultServiceProvider((ctx, options) => {
                        if (ctx.HostingEnvironment.IsDevelopment()) {
                            options.ValidateScopes = true;
                            options.ValidateOnBuild = true;
                        }
                    })
                    .UseStartup<Startup>()
                    .ConfigureServices(ConfigureAppServices)
                )
                .Build();
            return Task.CompletedTask;
        }

        public virtual async Task Initialize(bool shouldRecreateDb, CancellationToken cancellationToken = default)
        {
            var log = Host.Services.GetRequiredService<ILogger<AppHost>>();

            async Task InitializeOne(IDbInitializer dbInitializer, TaskSource<Unit> taskSource)
            {
                try {
                    log.LogInformation("{DbInitializer} started", dbInitializer.GetType().Name);
                    await dbInitializer.Initialize(cancellationToken);
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
                .ToDictionary(i => i, i => TaskSource.New<Unit>(true));
            var initializeTasks = initializeTaskSources
                .ToDictionary(kv => kv.Key, kv => (Task) kv.Value.Task);
            foreach (var (dbInitializer, _) in initializeTasks) {
                dbInitializer.ShouldRecreateDb = shouldRecreateDb;
                dbInitializer.InitializeTasks = initializeTasks;
            }
            var tasks = initializeTaskSources
                .Select(kv => InitializeOne(kv.Key, kv.Value))
                .ToArray();
            await Task.WhenAll(tasks);
            // await Task.Delay(100, cancellationToken); // Just in case
        }

        public virtual Task Start(CancellationToken cancellationToken = default)
            => Host.StartAsync(cancellationToken);

        public virtual Task Stop(CancellationToken cancellationToken = default)
            => Host.StopAsync(cancellationToken);

        public virtual Task Run(CancellationToken cancellationToken = default)
            => Host.RunAsync(cancellationToken);

        // Protected methods

        protected virtual void ConfigureHostConfiguration(IConfigurationBuilder cfg)
        {
            // Looks like there is no better way to set _default_ URL
            cfg.Sources.Insert(0, new MemoryConfigurationSource() {
                InitialData = new Dictionary<string, string>(StringComparer.Ordinal) {
                    {WebHostDefaults.ServerUrlsKey, ServerUrls},
                }
            });
            HostConfigurationBuilder?.Invoke(cfg);
        }

        protected virtual void ConfigureAppServices(
            WebHostBuilderContext webHost, IServiceCollection services)
            => AppServicesBuilder?.Invoke(webHost, services);
    }
}
