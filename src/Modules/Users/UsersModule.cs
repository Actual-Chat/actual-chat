using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Npgsql;
using Stl.Fusion.Operations.Internal;

namespace ActualChat.Users
{
    public class UsersModule : Module
    {
        public UsersModule(IServiceCollection services, IServiceProvider moduleBuilderServices)
            : base(services, moduleBuilderServices) { }

        public override void Use()
        {
            base.Use();
            var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
            var settings = Services.BuildServiceProvider().GetRequiredService<UsersSettings>();

            Services.AddDbContextFactory<UsersDbContext>(builder => {
                builder.UseNpgsql(settings.Db);
                if (isDevelopmentInstance)
                    builder.EnableSensitiveDataLogging();
            });
            Services.AddDbContextServices<UsersDbContext>(b => {
                Services.AddSingleton(new CompletionProducer.Options {
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
