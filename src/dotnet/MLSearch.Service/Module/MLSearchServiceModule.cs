using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.MLSearch.ApiAdapters;
using ActualChat.MLSearch.ApiAdapters.ShardWorker;
using ActualChat.MLSearch.Bot;
using ActualChat.MLSearch.Bot.External;
using ActualChat.MLSearch.Bot.Tools;
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
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography.X509Certificates;
// Note: Temporary disabled. Will be re-enabled with OpenAPI PR
// using Microsoft.OpenApi.Models;
using OpenSearch.Client;
using OpenSearch.Net;
using Microsoft.AspNetCore.Authentication;
using ActualChat.Module;
using Microsoft.AspNetCore.Builder;
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
        });

        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);

        // Module configuration

        services
            .ConfigureOpenSearch(Cfg, HostInfo)
            .AddWorkerPoolDependencies();

        // -- Register indexing common components --
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
        // Note: This is correct. ChatInfo must be indexed into the same index as ChatSlice for Join field to work
        services.AddSingleton<ISink<ChatInfo, string>>(static services
            => services.CreateInstanceWith<SemanticIndexSink<ChatInfo>>(IndexNames.ChatContent));

        services.AddSingleton<IChatInfoIndexer, ChatInfoIndexer>();
        services.AddSingleton<IChatContentIndexerFactory, ChatContentIndexerFactory>();
        services.AddSingleton<IChatContentIndexWorker>(static services
            => services.CreateInstanceWith<ChatContentIndexWorker>(
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

        var x509 = X509Certificate2.CreateFromPemFile(
            "/Users/andreykurochkin/Documents/Projects/work/actual.chat/actual-chat/example.com.crt",
            "/Users/andreykurochkin/Documents/Projects/work/actual.chat/actual-chat/example.com.key"
            //"/Users/andreykurochkin/Documents/Projects/work/actual.chat/actual-chat/sample.cert.pem"
            //,"/Users/andreykurochkin/Documents/Projects/work/actual.chat/actual-chat/sample.ecdsa"
        );
        if (this.Settings.Integrations == null) {
            this.Settings.Integrations = new MLIntegrations();
        }
        X509Certificate2 signingCertificate = new X509Certificate2(x509);
        var pubkey1 = signingCertificate.GetECDsaPublicKey();
        if (this.Settings.Integrations.Pubkeys == null) {
            this.Settings.Integrations.Pubkeys = new Dictionary<string, ECDsa>();
        }
        this.Settings.Integrations.Pubkeys.Add("chat-bot", pubkey1!);

       
        var privateKey = signingCertificate.GetECDsaPrivateKey();
        var securityKey = new ECDsaSecurityKey(privateKey);
        var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha384);
        services.AddSingleton<IBotConversationHandler>(services => 
            services.CreateInstanceWith<ExternalChatBotConversationHandler>(
                signingCredentials
            )
        );
        services.AddSingleton<IChatBotWorker>(static services
            => services.CreateInstanceWith<ChatBotWorker>(
            )
        );
        services.AddWorkerPool<IChatBotWorker, MLSearch_TriggerContinueConversationWithBot, ChatId, ChatId>(
            DuplicateJobPolicy.Drop, shardConcurrencyLevel: 10
        );
        // -- Register Controllers --
        //services.AddMvcCore().AddApplicationPart(GetType().Assembly);
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

        
        /*
        // -- Register JWT Authentication scheme --
        // TODO: Might be worth to discuss the integration part.
        var knownIntegrations = new Dictionary<string, ECDsa>();
        if (this.Settings.Integrations != null) {
            foreach (var (integration, pubkey) in this.Settings.Integrations.Pubkeys) {
                knownIntegrations.Add(integration, pubkey);
            }
        }
        // --
        
        
        
        foreach (var (inegration, pubkey) in knownIntegrations) {
            authentication.AddJwtBearer(options => {
                options.RequireHttpsMetadata = false;
                options.SaveToken = false;
                options.Audience = "integrations.actual.chat";
                options.TokenValidationParameters = new TokenValidationParameters {
                    ValidateAudience = true,
                    ValidateIssuer = true,
                    ValidIssuer = inegration,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new ECDsaSecurityKey(pubkey),
                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(3),
                    RequireSignedTokens = true,
                };
            });
        }
        */
        /*
        var authentication = services.AddAuthentication(AuthSchemes.BotAuthenticationScheme);
        authentication.AddScheme<BotAuthenticationSchemeOptions, BotAuthSchemeHandler>(
            AuthSchemes.BotAuthenticationScheme, e => {
                e.SigningCredentials = signingCredentials;
            }
        );

        services.AddAuthorization(options => {
            options.AddPolicy(BotConversationPolicy.Name, e => 
                BotConversationPolicy.Configure(e)
                    .AddAuthenticationSchemes(AuthSchemes.BotAuthenticationScheme)
                    .Build()
            );
        });
        services.AddAuthorization(options => {
            options.AddPolicy(BotToolsPolicy.Name, policy => 
                policy
                    .RequireAuthenticatedUser()
                    .AddAuthenticationSchemes(AuthSchemes.BotAuthenticationScheme)
                    .Build()
            );
        });
        */
        
        // This is a workaround. Read notes above the ConversationToolsController class
        services.Configure<BotAuthenticationSchemeOptions>(e=>{
            e.SigningCredentials = signingCredentials;
        });
        services.AddSingleton<BotAuthSchemeHandler>(services => {
            var options = services.GetRequiredService<IOptionsMonitor<BotAuthenticationSchemeOptions>>();
            return services.CreateInstanceWith<BotAuthSchemeHandler>(options);
        });

        // services.AddAuthorization();
        // Controllers, etc.
/*
        if (rpcHost.IsApiHost) {
            var mvcCore = services.AddMvcCore();
            
            mvcCore.AddApplicationPart(GetType().Assembly);
        }
        */
        services.AddMvcCore().AddApplicationPart(GetType().Assembly);
    }
}
