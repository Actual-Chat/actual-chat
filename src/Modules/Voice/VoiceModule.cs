using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Npgsql;
using Stl.Fusion.Extensions;
using Stl.Fusion.Operations.Internal;

namespace ActualChat.Voice
{
    public class VoiceModule : Module
    {
        public VoiceModule(IServiceCollection services, IServiceProvider moduleBuilderServices) 
            : base(services, moduleBuilderServices) { }

        public override void Use()
        {
            base.Use();
            
            var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
            var settings = Services.BuildServiceProvider().GetRequiredService<VoiceSettings>();

            var fusion = Services.AddFusion();
            fusion.AddSandboxedKeyValueStore();

            Services.AddDbContextFactory<VoiceDbContext>(builder => {
                builder.UseNpgsql(settings.Db);
                if (isDevelopmentInstance)
                    builder.EnableSensitiveDataLogging();
            });
            Services.AddDbContextServices<VoiceDbContext>(b => {
                Services.AddSingleton(new CompletionProducer.Options {
                    IsLoggingEnabled = true,
                });
            });
        }
    }
}