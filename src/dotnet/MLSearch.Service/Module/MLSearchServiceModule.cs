using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.MLSearch.ApiAdapters.ShardWorker;
using ActualChat.MLSearch.Bot;
using ActualChat.MLSearch.Bot.External;
using ActualChat.MLSearch.Bot.Tools;
using ActualChat.MLSearch.Db;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Indexing;
using ActualChat.MLSearch.Indexing.ChatContent;
using ActualChat.MLSearch.Indexing.Initializer;
using ActualChat.Redis.Module;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using OpenSearch.Client;
using OpenSearch.Net;
using Microsoft.AspNetCore.Authentication;
using ActualChat.Module;
using Microsoft.AspNetCore.Builder;
using ActualChat.MLSearch.Bot.Tools.Context;
using ActualChat.Search;
using ActualChat.Search.Db;
using Microsoft.Extensions.Hosting;
using IndexNames = ActualChat.MLSearch.Engine.IndexNames;

// Note: Temporary disabled. Will be re-enabled with OpenAPI PR
// using Swashbuckle.AspNetCore.SwaggerGen;

namespace ActualChat.MLSearch.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class MLSearchServiceModule(IServiceProvider moduleServices) : HostModule<MLSearchSettings>(moduleServices), IWebServerModule
{
    private readonly ILogger<MLSearchServiceModule> _log = moduleServices.LogFor<MLSearchServiceModule>();

    public void ConfigureApp(IApplicationBuilder app)
    {
        if (HostInfo.HasRole(HostRole.Api)) {
            app.UseEndpoints(endpoints => {
                endpoints.MapControllers();
            });
            app.UseRouting();
        }
    }
    protected override void InjectServices(IServiceCollection services)
    {
        if (!Settings.IsEnabled) {
            _log.LogInformation("MLSearch functionality is disabled, skipping service registrations");
            return;
        }

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<MLSearchDbContext>(services, Settings.Redis);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, MLSearchDbInitializer>();
        dbModule.AddDbContextServices<MLSearchDbContext>(services, Settings.Db, db => {
            db.AddEntityResolver<string, DbIndexedChat>();
            db.AddEntityResolver<string, DbContactIndexState>();
        });

        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        // TODO: isBackendClient

        // Module configuration

        services
            .ConfigureOpenSearch(Cfg, HostInfo)
            .AddWorkerPoolDependencies();

        // -- Register indexing common components --
        services.AddSingleton<IChatContentUpdateLoader>(
            static c => c.CreateInstance<ChatContentUpdateLoader>(
                100 // the size of a single batch of updates to load from db
            )
        );
        services.AddSingleton<ICursorStates<ChatContentCursor>>(
            static c => c.CreateInstance<CursorStates<ChatContentCursor>>(IndexNames.ChatContentCursor));

        // -- Register chat indexer --
        rpcHost.AddBackend<IChatIndexTrigger, ChatIndexTrigger>();

        services.AddSingleton<IChatContentDocumentLoader, ChatContentDocumentLoader>();
        services.AddSingleton<IChatContentMapper, ChatContentMapper>();
        services.AddSingleton<IChatContentArranger, ChatContentArranger>();

        services.AddSingleton<ISink<ChatSlice, string>>(
            static c => c.CreateInstance<SemanticIndexSink<ChatSlice>>(IndexNames.ChatContent));
        // Note: This is correct. ChatInfo must be indexed into the same index as ChatSlice for Join field to work
        services.AddSingleton<ISink<ChatInfo, string>>(
            static c => c.CreateInstance<SemanticIndexSink<ChatInfo>>(IndexNames.ChatContent));

        services.AddSingleton<IChatInfoIndexer, ChatInfoIndexer>();
        services.AddSingleton<IChatContentIndexerFactory, ChatContentIndexerFactory>();
        services.AddSingleton<IChatContentIndexWorker>(
            static c => c.CreateInstance<ChatContentIndexWorker>(
                75,  // a number of updates between flushes
                5000 // max number of updates to process in a single run
            )
        );
        services.AddWorkerPool<IChatContentIndexWorker, MLSearch_TriggerChatIndexing, (ChatId, IndexingKind), ChatId>(
            DuplicateJobPolicy.Drop, shardConcurrencyLevel: 10
        );

        if (Settings.IsInitialIndexingDisabled) {
            _log.LogInformation("Initial chat indexing is disabled, skipping services registration.");
        }
        else {
            // -- Register chat index initializer --
            rpcHost.AddBackend<IChatIndexInitializerTrigger, ChatIndexInitializerTrigger>();
            services.AddSingleton<ICursorStates<ChatIndexInitializerShard.Cursor>>(
                static c => c.CreateInstance<CursorStates<ChatIndexInitializerShard.Cursor>>(IndexNames.ChatCursor));
            services.AddSingleton<IInfiniteChatSequence, InfiniteChatSequence>();
            services.AddSingleton<IChatIndexInitializerShard, ChatIndexInitializerShard>();
            services.AddSingleton(
                    static c => c.CreateInstance<ChatIndexInitializer>(ShardScheme.MLSearchBackend))
                .AddAlias<IChatIndexInitializer, ChatIndexInitializer>()
                .AddAlias<IHostedService, ChatIndexInitializer>();
        }

        // -- Register ML bot --
        services.Configure<ChatBotConversationTriggerOptions>(e => {
            e.AllowPeerBotChat = (Settings.Integrations?.Bot?.AllowPeerBotChat) ?? false;
        });
        rpcHost.AddBackend<IChatBotConversationTrigger, ChatBotConversationTrigger>();
        if (Settings.Integrations != null){
            var x509 = X509Certificate2.CreateFromPemFile(
                Settings.Integrations.CertPemFilePath,
                Settings.Integrations.KeyPemFilePath
            );
            var privateKey = x509.GetECDsaPrivateKey();
            var securityKey = new ECDsaSecurityKey(privateKey);
            var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha384);

            // This is a workaround. Read notes above the ConversationToolsController class
            services.Configure<BotToolsContextHandlerOptions>(e => {
                e.Audience = Settings.Integrations.Audience;
                e.Issuer = Settings.Integrations.Issuer;
                e.SigningCredentials = signingCredentials;
                e.ContextLifetime = Settings.Integrations.ContextLifetime;
            });
            services.AddSingleton<IBotToolsContextHandler>(services => {
                var options = services.GetRequiredService<IOptionsMonitor<BotToolsContextHandlerOptions>>();
                return services.CreateInstance<BotToolsContextHandler>(options);
            });
            var isBotEnabled =
                Settings.IsEnabled
                && Settings.Integrations != null
                && Settings.Integrations.Bot != null
                && Settings.Integrations.Bot.IsEnabled
                && Settings.Integrations.Bot.WebHookUri != null;
            if (isBotEnabled) {
                services.Configure<ExternalChatbotSettings>( e => {
                    e.IsEnabled = true;
                    e.WebHookUri = Settings!.Integrations!.Bot!.WebHookUri!;
                });
                services.AddSingleton<IBotConversationHandler, ExternalChatBotConversationHandler>();
            }
        }
        services.AddSingleton<IChatBotWorker>(
            static c => c.CreateInstance<ChatBotWorker>());
        services.AddWorkerPool<IChatBotWorker, MLSearch_TriggerContinueConversationWithBot, ChatId, ChatId>(
            DuplicateJobPolicy.Drop, shardConcurrencyLevel: 10
        );
        // -- Register Controllers --
        services.AddMvcCore().AddApplicationPart(GetType().Assembly);
        // -- Register IMLSearchHandlers --
        rpcHost.AddApiOrLocal<IMLSearch, MLSearchImpl>();

        // -- Register Swagger endpoint (OpenAPI) --
        // Note: This is temporarily disabled. Will be re-enabled in a separate PR.
        /*
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c => {
            c.IncludeXmlComments(
                Path.Combine(
                    AppContext.BaseDirectory,
                    $"{Assembly.GetExecutingAssembly().GetName().Name}.xml"
                )
            );
            c.DocInclusionPredicate((docName, apiDesc) => {
                if (!apiDesc.TryGetMethodInfo(out MethodInfo methodInfo)) return false;
                var isBotTool = methodInfo.DeclaringType
                    .GetCustomAttributes(true)
                    .OfType<BotToolsAttribute>()
                    .Any();
                return isBotTool;
            });
            c.SwaggerDoc("bot-tools-v1", new OpenApiInfo { Title = "Bot Tools API - V1", Version = "v1"});
        });
        */

        // TODO: put in a proper place in this file after merging PRs
        // Search
        rpcHost.AddApi<ISearch, Search.Search>();
        rpcHost.AddBackend<ISearchBackend, SearchBackend>();

        // Indexing
        rpcHost.AddBackend<IIndexedChatsBackend, IndexedChatsBackend>();
        rpcHost.AddBackend<IContactIndexStatesBackend, ContactIndexStateBackend>();

        // Internal services
        // TODO: uncomment when migration to single index is done
        // services.AddSingleton<TextEntryIndexer>()
        //     .AddHostedService(c => c.GetRequiredService<TextEntryIndexer>());
        services.AddSingleton<UserContactIndexer>()
            .AddHostedService(c => c.GetRequiredService<UserContactIndexer>());
        services.AddSingleton<GroupChatContactIndexer>()
            .AddHostedService(c => c.GetRequiredService<GroupChatContactIndexer>());
        services.AddSingleton<PlaceContactIndexer>()
            .AddHostedService(c => c.GetRequiredService<PlaceContactIndexer>());

        services.AddSingleton<OpenSearchConfigurator>()
            .AddHostedService(c => c.GetRequiredService<OpenSearchConfigurator>());
        // TODO: merge into single IndexNames
        services.AddSingleton<Search.IndexNames>();
    }
}
