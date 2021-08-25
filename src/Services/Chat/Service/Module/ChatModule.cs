using System;
using System.Data;
using System.Linq.Expressions;
using ActualChat.Chat.Db;
using ActualChat.Chat.Markup;
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

namespace ActualChat.Chat.Module
{
    public class ChatModule : HostModule
    {
        public ChatModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public ChatModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
                return; // Server-side only module

            var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
            services.AddSettings<ChatSettings>();
            var settings = services.BuildServiceProvider().GetRequiredService<ChatSettings>();
            services.AddSingleton<IDataInitializer, ChatDbInitializer>();

            var fusion = services.AddFusion();
            services.AddDbContextFactory<ChatDbContext>(builder => {
                builder.UseNpgsql(settings.Db);
                if (isDevelopmentInstance)
                    builder.EnableSensitiveDataLogging();
            });
            services.AddDbContextServices<ChatDbContext>(dbContext => {
                services.AddSingleton(new CompletionProducer.Options {
                    IsLoggingEnabled = true,
                });
                services.AddTransient(c => new DbOperationScope<ChatDbContext>(c) {
                    IsolationLevel = IsolationLevel.Serializable,
                });
                dbContext.AddOperations((_, o) => {
                    o.UnconditionalWakeUpPeriod = TimeSpan.FromSeconds(isDevelopmentInstance ? 60 : 5);
                });
                dbContext.AddNpgsqlOperationLogChangeTracking();

                dbContext.AddEntityResolver<string, DbChat>((_, options) => {
                    options.QueryTransformer = dbChats => dbChats.Include(chat => chat.Owners);
                });
                dbContext.AddEntityResolver<string, DbChatEntry>();
            });
            services.AddCommander().AddHandlerFilter((handler, commandType) => {
                // 1. Check if this is DbOperationScopeProvider<AudioDbContext> handler
                if (handler is not InterfaceCommandHandler<ICommand> ich)
                    return true;
                if (ich.ServiceType != typeof(DbOperationScopeProvider<ChatDbContext>))
                    return true;
                // 2. Make sure it's intact only for local commands
                var commandAssembly = commandType.Assembly;
                if (commandAssembly == typeof(Chat).Assembly)
                    return true;
                return false;
            });

            services.AddMvc().AddApplicationPart(GetType().Assembly);
            services.AddSingleton<IMarkupParser, MarkupParser>();
            fusion.AddComputeService<ChatService>();
            services.AddSingleton(c => (IChatService) c.GetRequiredService<ChatService>());
            services.AddSingleton(c => (IServerSideChatService) c.GetRequiredService<ChatService>());
        }
    }
}
