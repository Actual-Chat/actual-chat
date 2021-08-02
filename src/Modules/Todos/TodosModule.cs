using System;
using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Npgsql;
using Stl.Fusion.Extensions;
using Stl.Fusion.Operations.Internal;
using Stl.Plugins;

namespace ActualChat.Todos
{
    public class TodosModule : HostModule
    {
        public TodosModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public TodosModule(IServiceProvider services) : base(services) { }

        public override void InjectServices(IServiceCollection services)
        {
            base.InjectServices(services);
            var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
            var settings = services.BuildServiceProvider().GetRequiredService<TodosSettings>();

            var fusion = services.AddFusion();
            fusion.AddSandboxedKeyValueStore();

            services.AddDbContextFactory<TodosDbContext>(builder => {
                builder.UseNpgsql(settings.Db);
                if (isDevelopmentInstance)
                    builder.EnableSensitiveDataLogging();
            });
            services.AddDbContextServices<TodosDbContext>(b => {
                services.AddSingleton(new CompletionProducer.Options {
                    IsLoggingEnabled = true,
                });
                b.AddDbOperations((_, o) => {
                    o.UnconditionalWakeUpPeriod = TimeSpan.FromSeconds(isDevelopmentInstance ? 60 : 5);
                });
                b.AddNpgsqlDbOperationLogChangeTracking();

                b.AddKeyValueStore();
            });
        }
    }
}
