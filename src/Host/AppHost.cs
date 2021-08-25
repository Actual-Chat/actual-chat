using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ActualChat.Hosting;
using Microsoft.Extensions.Configuration;

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

        public virtual async Task Initialize(bool recreate, CancellationToken cancellationToken = default)
        {
            var dbInitializers = Host.Services.GetServices<IDataInitializer>();
            var initTasks = dbInitializers.Select(i => i.Initialize(recreate, cancellationToken)).ToArray();
            await Task.WhenAll(initTasks);
            await Task.Delay(100, cancellationToken); //
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
                InitialData = new Dictionary<string, string>() {
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
