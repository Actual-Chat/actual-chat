using System;
using ActualChat.Audio.Db;
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
using Stl.Fusion.Extensions;
using Stl.Fusion.Operations.Internal;
using Stl.Plugins;

namespace ActualChat.Audio.Module
{
    public class AudioModule : HostModule
    {
        public AudioModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public AudioModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (HostInfo.ServiceScope != ServiceScope.Server)
                return; // Server-side only module

            base.InjectServices(services);
            var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
            var settings = services.BuildServiceProvider().GetRequiredService<AudioSettings>();

            var fusion = services.AddFusion();
            fusion.AddSandboxedKeyValueStore();

            services.AddDbContextFactory<AudioDbContext>(builder => {
                builder.UseNpgsql(settings.Db);
                if (isDevelopmentInstance)
                    builder.EnableSensitiveDataLogging();
            });
            services.AddDbContextServices<AudioDbContext>(b => {
                services.AddSingleton(new CompletionProducer.Options {
                    IsLoggingEnabled = true,
                });
                b.AddDbOperations((_, o) => {
                    o.UnconditionalWakeUpPeriod = TimeSpan.FromSeconds(isDevelopmentInstance ? 60 : 5);
                });
                b.AddNpgsqlDbOperationLogChangeTracking();
            });
            services.AddCommander().AddHandlerFilter((handler, commandType) => {
                // 1. Check if this is DbOperationScopeProvider<AudioDbContext> handler
                if (handler is not InterfaceCommandHandler<ICommand> ich)
                    return true;
                if (ich.ServiceType != typeof(DbOperationScopeProvider<AudioDbContext>))
                    return true;
                // 2. Make sure it's intact only for local commands
                var commandAssembly = commandType.Assembly;
                if (commandAssembly == typeof(IAudioRecorder).Assembly)
                    return true;
                return false;
            });
        }
    }
}
