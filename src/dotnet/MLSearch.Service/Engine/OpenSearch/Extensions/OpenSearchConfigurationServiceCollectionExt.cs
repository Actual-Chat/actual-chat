using System.Security.Cryptography.X509Certificates;
using ActualChat.Hosting;
using ActualChat.Mesh;
using ActualChat.MLSearch.Db;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Configuration;
using ActualChat.MLSearch.Engine.OpenSearch.Serializer;
using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using ActualChat.MLSearch.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OpenSearch.Client;
using OpenSearch.Net;

namespace ActualChat.MLSearch.Engine.OpenSearch.Extensions;

internal static class OpenSearchConfigurationServiceCollectionExt
{
    public static IServiceCollection ConfigureOpenSearch(
        this IServiceCollection services,
        IConfiguration cfg,
        HostInfo hostInfo)
    {
        services.AddOptionsWithValidateOnStart<OpenSearchSettings>()
            .Bind(cfg.GetSection($"{nameof(MLSearchSettings)}:{MLSearchSettings.OpenSearch}"))
            .ValidateDataAnnotations()
            .Validate(options => Uri.IsWellFormedUriString(options.ClusterUri, UriKind.Absolute),
                $"Value for {nameof(OpenSearchSettings.ClusterUri)} must be valid URI.")
            .PostConfigure(options => {
                if (options.DefaultNumberOfReplicas is null && hostInfo.IsDevelopmentInstance) {
                    options.DefaultNumberOfReplicas = 0;
                }
            });

        services.AddSingleton<OpenSearchNames>();
        services.AddSingleton(_ => new OpenSearchNamingPolicy(JsonNamingPolicy.CamelCase));

        services.AddSingleton<IOpenSearchClient>(s => {
            var openSearchSettings = s.GetRequiredService<IOptions<OpenSearchSettings>>().Value;
            var connectionSettings = new ConnectionSettings(
                    new SingleNodeConnectionPool(new Uri(openSearchSettings.ClusterUri)),
                    sourceSerializer: (builtin, settings) => new OpenSearchJsonSerializer(builtin, settings, typeInfoModifiers: [
                        ChatsTypeInfoModifier.Modify,
                    ]))
                .DefaultFieldNameInferrer(JsonNamingPolicy.CamelCase.ConvertName)
                .DefaultMappingFor<ChatInfo>(map => map.RelationName(ChatInfoToChatSliceRelation.ChatInfoName))
                .DefaultMappingFor<ChatSlice>(map => map.RelationName(ChatInfoToChatSliceRelation.ChatSliceName))
                .DefaultMappingFor<IndexedChat>(map => map.RoutingProperty(x => x.Id))
                .DefaultMappingFor<IndexedEntry>(map => map.RoutingProperty(x => x.ChatId));
            if (!openSearchSettings.ClientCertificatePath.IsNullOrEmpty()) {
                var certPath = Path.Combine(openSearchSettings.ClientCertificatePath, "tls.crt");
                var keyPath = Path.Combine(openSearchSettings.ClientCertificatePath, "tls.key");
                connectionSettings.ClientCertificate(X509Certificate2.CreateFromPemFile(certPath, keyPath));
            }
            return new OpenSearchClient(connectionSettings);
        });

        services.AddSingleton<ServiceCoordinator>()
            .AddAlias<IServiceCoordinator, ServiceCoordinator>()
            .AddHostedService(c => c.GetRequiredService<ServiceCoordinator>());

        services
            .AddSingleton<IClusterSetup>(static c => c.CreateInstance<ClusterSetup>(
                c.GetRequiredService<IMeshLocks<MLSearchDbContext>>().WithKeyPrefix(nameof(ClusterSetup))
            ))
            .AddSingleton<IClusterSetupActions, ClusterSetupActions>();

        services
            .AddSingleton<IOptionsFactory<PlainIndexSettings>, PlainIndexSettingsFactory>()
            .AddSingleton<IOptionsFactory<SemanticIndexSettings>, SemanticIndexSettingsFactory>();

        services
            .AddSingleton(
                static c => c.CreateInstance<SettingsChangeTokenSource<SemanticIndexSettings>>(OpenSearchNames.ChatContent))
            .AddAlias<ISettingsChangeTokenSource, SettingsChangeTokenSource<SemanticIndexSettings>>()
            .AddAlias<IOptionsChangeTokenSource<SemanticIndexSettings>, SettingsChangeTokenSource<SemanticIndexSettings>>();

        foreach (var indexName in new[] { OpenSearchNames.ChatCursor, OpenSearchNames.ChatContentCursor }) {
            services
                .AddSingleton(c => c.CreateInstance<SettingsChangeTokenSource<PlainIndexSettings>>(indexName))
                .AddAlias<ISettingsChangeTokenSource, SettingsChangeTokenSource<PlainIndexSettings>>()
                .AddAlias<IOptionsChangeTokenSource<PlainIndexSettings>, SettingsChangeTokenSource<PlainIndexSettings>>();
        }

        // ChatSlice engine registrations
        services.AddSingleton<ISearchEngine<ChatSlice>>(
            static c => c.CreateInstance<SemanticSearchEngine<ChatSlice>>(OpenSearchNames.ChatContent));

        return services;
    }
}
