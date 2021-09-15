using System;
using System.Data;
using ActualChat.Audio.Db;
using ActualChat.Audio.Orchestration;
using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.CommandR;
using Stl.CommandR.Configuration;
using Stl.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Npgsql;
using Stl.Fusion.EntityFramework.Operations;
using Stl.Fusion.Operations.Internal;
using Stl.Plugins;

namespace ActualChat.Audio.Module
{
    public class AudioModule : HostModule<AudioSettings>
    {
        public AudioModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public AudioModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            base.InjectServices(services);
            if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
                return; // Server-side only module

            services.AddSingleton<IDbInitializer, AudioDbInitializer>();

            var fusion = services.AddFusion();
            services.AddDbContextFactory<AudioDbContext>(builder => {
                builder.UseNpgsql(Settings.Db);
                if (IsDevelopmentInstance)
                    builder.EnableSensitiveDataLogging();
            });
            services.AddDbContextServices<AudioDbContext>(dbContext => {
                services.AddSingleton(new CompletionProducer.Options {
                    IsLoggingEnabled = true,
                });
                services.AddTransient(c => new DbOperationScope<AudioDbContext>(c) {
                    IsolationLevel = IsolationLevel.RepeatableRead,
                });
                dbContext.AddOperations((_, o) => {
                    o.UnconditionalWakeUpPeriod = TimeSpan.FromSeconds(IsDevelopmentInstance ? 60 : 5);
                });
                dbContext.AddNpgsqlOperationLogChangeTracking();
            });
            services.AddCommander().AddHandlerFilter((handler, commandType) => {
                // 1. Check if this is DbOperationScopeProvider<AudioDbContext> handler
                if (handler is not InterfaceCommandHandler<ICommand> ich)
                    return true;
                if (ich.ServiceType != typeof(DbOperationScopeProvider<AudioDbContext>))
                    return true;
                // 2. Make sure it's intact only for local commands
                var commandAssembly = commandType.Assembly;
                if (commandAssembly == typeof(IAudio).Assembly)
                    return true;
                return false;
            });

            // Module's own services
            services.AddSingleton<AudioSaver>();
            services.AddSingleton<AudioActivityExtractor>();
            services.AddSingleton<AudioOrchestrator>();
            services.AddHostedService(sp => sp.GetRequiredService<AudioOrchestrator>());
        }
    }
}
