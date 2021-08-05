using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.Extensions;
using Stl.Fusion.Operations.Internal;
using Stl.Plugins;

namespace ActualChat.Chat.Module
{
    public class ChatModule : HostModule
    {
        public ChatModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public ChatModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (HostInfo.ServiceScope != ServiceScope.Server)
                return; // Server-side only module

            base.InjectServices(services);
            var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
            var settings = services.BuildServiceProvider().GetRequiredService<ChatSettings>();

            var fusion = services.AddFusion();
            fusion.AddSandboxedKeyValueStore();

            services.AddDbContextFactory<ChatDbContext>(builder => {
                builder.UseNpgsql(settings.Db);
                if (isDevelopmentInstance)
                    builder.EnableSensitiveDataLogging();
            });
            services.AddDbContextServices<ChatDbContext>(b => {
                services.AddSingleton(new CompletionProducer.Options {
                    IsLoggingEnabled = true,
                });
            });
        }
    }
}
