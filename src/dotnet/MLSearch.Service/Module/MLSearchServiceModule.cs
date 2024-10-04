using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using ActualChat.Chat.ML;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Integrations.Anthropic;
using ActualChat.MLSearch.ApiAdapters.ShardWorker;
using ActualChat.MLSearch.Bot;
using ActualChat.MLSearch.Bot.External;
using ActualChat.MLSearch.Db;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Indexing;
using ActualChat.MLSearch.Indexing.ChatContent;
using ActualChat.MLSearch.Indexing.Initializer;
using ActualChat.Redis.Module;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ActualChat.Module;
using Microsoft.AspNetCore.Builder;
using ActualChat.MLSearch.Bot.Tools.Context;
using ActualChat.Rpc;
using ActualChat.Search;
using IndexedEntry = ActualChat.MLSearch.Documents.IndexedEntry;

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

        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<ISearchBackend>().IsClient();

        rpcHost.AddApi<ISearch, Search>();
        rpcHost.AddApi<IMLSearch, MLSearchImpl>();
        rpcHost.AddBackend<ISearchBackend, SearchBackend>();
        rpcHost.AddBackend<IContactIndexStatesBackend, ContactIndexStateBackend>();
        rpcHost.AddBackend<IChatIndexTrigger, ChatIndexTrigger>();
        rpcHost.AddBackend<IMLSearchBackend, MLSearchBackend>();
        InjectIndexingServices(rpcHost, isBackendClient);
        InjectBotServices(rpcHost, isBackendClient);

        if (Settings.IsInitialIndexingDisabled) {
            _log.LogInformation("Initial chat indexing is disabled, skipping services registration");
        }
        else {
            _log.LogInformation("Initial chat indexing is enabled");
            rpcHost.AddBackend<IChatIndexInitializerTrigger, ChatIndexInitializerTrigger>();
            if (!isBackendClient) {
                services.AddSingleton<ICursorStates<ChatIndexInitializerShard.Cursor>>(
                    static c => c.CreateInstance<CursorStates<ChatIndexInitializerShard.Cursor>>(OpenSearchNames.ChatCursor));
                services.AddSingleton<IInfiniteChatSequence, InfiniteChatSequence>();
                services.AddSingleton<IChatIndexInitializerShard, ChatIndexInitializerShard>();
                services.AddSingleton(static c => c.CreateInstance<ChatIndexInitializer>(ShardScheme.MLSearchBackend))
                    .AddAlias<IChatIndexInitializer, ChatIndexInitializer>()
                    .AddAlias<IHostedService, ChatIndexInitializer>();
            }
        }

        if (isBackendClient)
            return;

        // Shared backend services

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<MLSearchDbContext>(services, Settings.Redis);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, MLSearchDbInitializer>();
        dbModule.AddDbContextServices<MLSearchDbContext>(services, Settings.Db, db => {
            db.AddEntityResolver<string, DbContactIndexState>();
        });

        // OpenSearch
        services
            .ConfigureOpenSearch(Cfg, HostInfo)
            .AddWorkerPoolDependencies();
        services.AddSingleton<OpenSearchConfigurator>()
            .AddHostedService(c => c.GetRequiredService<OpenSearchConfigurator>());
    }

    private static void InjectIndexingServices(RpcHostBuilder rpcHost, bool isBackendClient)
    {
        var services = rpcHost.Services;
        if (isBackendClient)
            return;

        // Common indexing components

        services.AddSingleton<IChatContentUpdateLoader>(
            static c => c.CreateInstance<ChatContentUpdateLoader>(
                100 // the size of a single batch of updates to load from db
            )
        );
        services.AddSingleton<ICursorStates<ChatContentCursor>>(
            static c => c.CreateInstance<CursorStates<ChatContentCursor>>(OpenSearchNames.ChatContentCursor));
        services.AddSingleton<ICursorStates<ChatEntryCursor>>(
            static c => c.CreateInstance<CursorStates<ChatEntryCursor>>(c.GetRequiredService<OpenSearchNames>().EntryCursorIndexName));

        // Contact indexing: UserContactIndexer, GroupChatContactIndexer, PlaceContactIndexer

        services.AddSingleton<UserContactIndexer>()
            .AddHostedService(c => c.GetRequiredService<UserContactIndexer>());
        services.AddSingleton<GroupChatContactIndexer>()
            .AddHostedService(c => c.GetRequiredService<GroupChatContactIndexer>());
        services.AddSingleton<PlaceContactIndexer>()
            .AddHostedService(c => c.GetRequiredService<PlaceContactIndexer>());

        // Chat content indexing

        // Common types
        services.AddSingleton<IChatContentDocumentLoader, ChatContentDocumentLoader>();
        services.AddSingleton<IChatContentMapper, ChatContentMapper>();
        services.AddSingleton(_ => new DialogFragmentAnalyzer.Options { IsDiagnosticsEnabled = true });
        services.AddSingleton<IDialogFragmentAnalyzer, DialogFragmentAnalyzer>();
        services.AddSingleton<ChatContentArranger>();
        services.AddSingleton<ChatContentArranger2>();
        services.AddAlias<IChatContentArranger, ChatContentArranger>(ServiceLifetime.Scoped);
        services.AddSingleton<IChatInfoIndexer, ChatInfoIndexer>();
        services.AddSingleton<IChatContentIndexerFactory, ChatContentIndexerFactory>();
        services.AddSingleton<IChatContentArrangerSelector, ChatContentArrangerSelector>();

        // Indexing sinks
        services.AddSingleton<ISink<ChatSlice, string>>(
            static c => c.CreateInstance<SemanticIndexSink<ChatSlice>>(OpenSearchNames.ChatContent));
        // Note: This is correct. ChatInfo  must be indexed into the same index as ChatSlice and ChatEntry for Join field to work
        services.AddSingleton<ISink<ChatInfo, string>>(
            static c => c.CreateInstance<SemanticIndexSink<ChatInfo>>(OpenSearchNames.ChatContent));
        services.AddSingleton<ISink<IndexedEntry, TextEntryId>>(
            static c => c.CreateInstance<IndexSink<IndexedEntry, TextEntryId>>(c.GetRequiredService<OpenSearchNames>().EntryIndexName));
        services.AddSingleton<ISink<IndexedChat, ChatId>>(
            static c => c.CreateInstance<IndexSink<IndexedChat, ChatId>>(c.GetRequiredService<OpenSearchNames>().EntryIndexName));

        // Workers
        services.AddSingleton<IChatContentIndexWorker>(
            static c => c.CreateInstance<ChatContentIndexWorker>(
                75,  // a number of updates between flushes
                5000 // max number of updates to process in a single run
            )
        );
        services.AddWorkerPool<IChatContentIndexWorker, MLSearch_TriggerChatIndexing, (ChatId, IndexingKind), ChatId>(
            DuplicateJobPolicy.Drop, shardConcurrencyLevel: 10
        );
        services.AddSingleton<ChatEntryIndexWorker>();
        services.AddWorkerPool<ChatEntryIndexWorker, MLSearch_TriggerChatIndexing, (ChatId, IndexingKind), ChatId>(
            DuplicateJobPolicy.Drop, shardConcurrencyLevel: 10
        );

        // Other
        services.AddChatMLServices();
        services.AddAnthropicServices();
    }

    private void InjectBotServices(RpcHostBuilder rpcHost, bool isBackendClient)
    {
        var services = rpcHost.Services;

        if (rpcHost.IsApiHost) {
            services.AddMvcCore().AddApplicationPart(GetType().Assembly); // Controllers

            // Swagger endpoint (OpenAPI)
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
        }

        if (isBackendClient)
            return;

        // NOTE(AY): Technically tool controllers should just forward the calls to backend APIs,
        // so I assume what's below shouldn't be available on ApiHost / front-end.

        services.Configure<ChatBotConversationTriggerOptions>(e => {
            e.AllowPeerBotChat = Settings.Integrations?.Bot?.AllowPeerBotChat ?? false;
        });
        if (Settings.Integrations != null) {
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
            services.AddSingleton<IBotToolsContextHandler>(c => {
                var options = c.GetRequiredService<IOptionsMonitor<BotToolsContextHandlerOptions>>();
                return c.CreateInstance<BotToolsContextHandler>(options);
            });
            var isBotEnabled =
                Settings is { IsEnabled: true, Integrations.Bot.IsEnabled: true }
                && Settings.Integrations.Bot.WebHookUri != null!;
            if (isBotEnabled) {
                rpcHost.AddBackend<IChatBotConversationTrigger, ChatBotConversationTrigger>();
                services.Configure<ExternalChatbotSettings>( e => {
                    e.IsEnabled = true;
                    e.WebHookUri = Settings!.Integrations!.Bot!.WebHookUri!;
                });
                services.AddSingleton<IFilters, Filters>();
                services.AddSingleton<IBotConversationHandler, ExternalChatBotConversationHandler>();
                services.AddSingleton<IChatBotWorker>(
                    static c => c.CreateInstance<ChatBotWorker>());
                services.AddWorkerPool<IChatBotWorker, MLSearch_TriggerContinueConversationWithBot, ChatId, ChatId>(
                    DuplicateJobPolicy.Drop, shardConcurrencyLevel: 10
                );
            }
        }
    }
}
