using System;
using System.Data;
using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.CommandR;
using Stl.CommandR.Configuration;
using Stl.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.Authentication;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Npgsql;
using Stl.Fusion.EntityFramework.Operations;
using Stl.Fusion.Extensions;
using Stl.Fusion.Extensions.Commands;
using Stl.Fusion.Operations.Internal;
using Stl.Plugins;

namespace ActualChat.Todos.Module
{
    public class TodosModule : HostModule
    {
        public TodosModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public TodosModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
                return; // Server-side only module

            var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
            services.AddSettings<TodosSettings>();
            var settings = services.BuildServiceProvider().GetRequiredService<TodosSettings>();
            services.AddSingleton<IDbInitializer, TodosDbInitializer>();

            var fusion = services.AddFusion();
            services.AddDbContextFactory<TodosDbContext>(builder => {
                builder.UseNpgsql(settings.Db);
                if (isDevelopmentInstance)
                    builder.EnableSensitiveDataLogging();
            });
            services.AddDbContextServices<TodosDbContext>(dbContext => {
                services.AddSingleton(new CompletionProducer.Options {
                    IsLoggingEnabled = true,
                });
                services.AddTransient(c => new DbOperationScope<TodosDbContext>(c) {
                    IsolationLevel = IsolationLevel.Snapshot,
                });
                dbContext.AddOperations((_, o) => {
                    o.UnconditionalWakeUpPeriod = TimeSpan.FromSeconds(isDevelopmentInstance ? 60 : 5);
                });
                dbContext.AddNpgsqlOperationLogChangeTracking();
                dbContext.AddKeyValueStore();
                fusion.AddSandboxedKeyValueStore();
            });
            services.AddCommander().AddHandlerFilter((handler, commandType) => {
                // 1. Check if this is DbOperationScopeProvider<TodosDbContext> handler
                if (handler is not InterfaceCommandHandler<ICommand> ich)
                    return true;
                if (ich.ServiceType != typeof(DbOperationScopeProvider<TodosDbContext>))
                    return true;
                // 2. Make sure it's intact only for Stl.Fusion.Extension.Commands & local commands
                var commandAssembly = commandType.Assembly;
                if (commandAssembly == typeof(SetCommand).Assembly && commandType.Namespace == typeof(SetCommand).Namespace)
                    return true;
                if (commandAssembly == typeof(ITodoService).Assembly)
                    return true;
                return false;
            });

            // Module's own services
            services.AddMvc().AddApplicationPart(GetType().Assembly);
            fusion.AddComputeService<ITodoService, TodoService>();
        }
    }
}
