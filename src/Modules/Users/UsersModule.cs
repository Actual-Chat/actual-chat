using System;
using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Npgsql;
using Stl.Fusion.Operations.Internal;
using Stl.Plugins;

namespace ActualChat.Users
{
    public class UsersHostModule : HostModule
    {
        public UsersHostModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public UsersHostModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (HostInfo.ServiceScope != ServiceScope.Server)
                return; // Server-side only module

            base.InjectServices(services);
            var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
            var settings = services.BuildServiceProvider().GetRequiredService<UsersSettings>();

            services.AddDbContextFactory<UsersDbContext>(builder => {
                builder.UseNpgsql(settings.Db);
                if (isDevelopmentInstance)
                    builder.EnableSensitiveDataLogging();
            });
            services.AddDbContextServices<UsersDbContext>(b => {
                services.AddSingleton(new CompletionProducer.Options {
                    IsLoggingEnabled = true,
                });
                b.AddDbOperations((_, o) => {
                    o.UnconditionalWakeUpPeriod = TimeSpan.FromSeconds(isDevelopmentInstance ? 60 : 5);
                });
                b.AddNpgsqlDbOperationLogChangeTracking();

                b.AddDbAuthentication((_, options) => {
                    options.MinUpdatePresencePeriod = TimeSpan.FromSeconds(55);
                });
            });
        }
    }
}
