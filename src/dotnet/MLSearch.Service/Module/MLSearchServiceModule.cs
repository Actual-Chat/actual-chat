using System.Diagnostics.CodeAnalysis;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.MLSearch.ApiAdapters;
using ActualChat.MLSearch.ApiAdapters.ShardWorker;
using ActualChat.MLSearch.Bot;
using ActualChat.MLSearch.Db;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Engine.OpenSearch;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using ActualChat.MLSearch.Indexing;
using ActualChat.MLSearch.Indexing.ChatContent;
using ActualChat.MLSearch.Indexing.Initializer;
using ActualChat.Redis.Module;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
// Note: Temporary disabled. Will be re-enabled with OpenAPI PR
// using Microsoft.OpenApi.Models;
using OpenSearch.Client;
using OpenSearch.Net;
// Note: Temporary disabled. Will be re-enabled with OpenAPI PR
// using Swashbuckle.AspNetCore.SwaggerGen;

namespace ActualChat.MLSearch.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class MLSearchServiceModule(IServiceProvider moduleServices) : HostModule<MLSearchSettings>(moduleServices)
{
    private readonly ILogger<MLSearchServiceModule> _log = moduleServices.LogFor<MLSearchServiceModule>();

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.HostKind.IsServer()) {
            return; // Server-side only module
        }
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
        });

        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);

        // Module configuration

        services
            .ConfigureOpenSearch(Cfg, HostInfo)
            .AddWorkerPoolDependencies();

        // -- Register indexing common components --
        services.AddSingleton<IChatIndexFilter, ChatIndexFilter>();
        services.AddSingleton<IChatContentUpdateLoader>(static services
            => services.CreateInstanceWith<ChatContentUpdateLoader>(
                100 // the size of a single batch of updates to load from db
            )
        );
        services.AddSingleton<ICursorStates<ChatContentCursor>>(static services
            => services.CreateInstanceWith<CursorStates<ChatContentCursor>>(IndexNames.ChatContentCursor));

        // -- Register chat indexer --
        rpcHost.AddBackend<IChatIndexTrigger, ChatIndexTrigger>();

        services.AddSingleton<IChatContentDocumentLoader, ChatContentDocumentLoader>();
        services.AddSingleton<IChatContentMapper, ChatContentMapper>();
        services.AddSingleton<IChatContentArranger, ChatContentArranger>();

        services.AddSingleton<ISink<ChatSlice, string>>(static services
            => services.CreateInstanceWith<SemanticIndexSink<ChatSlice>>(IndexNames.ChatContent));

        services.AddSingleton<IChatContentIndexerFactory, ChatContentIndexerFactory>();
        services.AddSingleton<IChatContentIndexWorker>(static services
            => services.CreateInstanceWith<ChatContentIndexWorker>(
                75,  // a number of updates between flushes
                5000 // max number of updates to process in a single run
            )
        );
        services.AddWorkerPool<IChatContentIndexWorker, MLSearch_TriggerChatIndexing, ChatId, ChatId>(
            DuplicateJobPolicy.Drop, shardConcurrencyLevel: 10
        );

        if (Settings.IsInitialIndexingDisabled) {
            _log.LogInformation("Initial chat indexing is disabled, skipping services registration.");
        }
        else {
            // -- Register chat index initializer --
            rpcHost.AddBackend<IChatIndexInitializerTrigger, ChatIndexInitializerTrigger>();
            services.AddSingleton<ICursorStates<ChatIndexInitializerShard.Cursor>>(static services
                => services.CreateInstanceWith<CursorStates<ChatIndexInitializerShard.Cursor>>(IndexNames.ChatCursor));
            services.AddSingleton<IInfiniteChatSequence, InfiniteChatSequence>();
            services.AddSingleton<IChatIndexInitializerShard, ChatIndexInitializerShard>();
            services.AddSingleton(static services
                => services.CreateInstanceWith<ChatIndexInitializer>(
                    ShardScheme.MLSearchBackend))
                .AddAlias<IChatIndexInitializer, ChatIndexInitializer>()
                .AddAlias<IHostedService, ChatIndexInitializer>();
        }

        // -- Register ML bot --
//        const string ConversationBotServiceGroup = "ML Chat Bot";
        rpcHost.AddBackend<IChatBotConversationTrigger, ChatBotConversationTrigger>();

        services.AddSingleton<IBotConversationHandler, SampleChatBot>();
        services.AddSingleton<IChatBotWorker>(static services
            => services.CreateInstanceWith<ChatBotWorker>(
            )
        );
        services.AddWorkerPool<IChatBotWorker, MLSearch_TriggerContinueConversationWithBot, ChatId, ChatId>(
            DuplicateJobPolicy.Drop, shardConcurrencyLevel: 10
        );
        // -- Register Controllers --
        services.AddMvcCore().AddApplicationPart(GetType().Assembly);
        // -- Register IMLSearchHanders --
        rpcHost.AddApiOrLocal<IMLSearch, MLSearchImpl>();


        // -- Register Swagger endpoint (OpenAPI) --
        // Note: This is temporary disabled. Will be re-enabled in a separate PR.
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
    }
}
