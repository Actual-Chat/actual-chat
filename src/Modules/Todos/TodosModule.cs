using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Npgsql;
using Stl.Fusion.Extensions;
using Stl.Fusion.Operations.Internal;

namespace ActualChat.Todos
{
    public class TodosModule : Module
    {
        public TodosModule(IServiceCollection services, IServiceProvider moduleBuilderServices)
            : base(services, moduleBuilderServices) { }

        public override void Use()
        {
            base.Use();
            var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
            var settings = Services.BuildServiceProvider().GetRequiredService<TodosSettings>();

            var fusion = Services.AddFusion();
            fusion.AddSandboxedKeyValueStore();

            Services.AddDbContextFactory<TodosDbContext>(builder => {
                builder.UseNpgsql(settings.Db);
                if (isDevelopmentInstance)
                    builder.EnableSensitiveDataLogging();
            });
            Services.AddDbContextServices<TodosDbContext>(b => {
                Services.AddSingleton(new CompletionProducer.Options {
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
