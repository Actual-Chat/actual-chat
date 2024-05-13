using ActualChat.Hosting;
using ActualChat.Mesh;
using ActualChat.MLSearch.ApiAdapters;
using ActualChat.MLSearch.Db;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Configuration;
using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using ActualChat.MLSearch.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OpenSearch.Client;
using OpenSearch.Net;

namespace ActualChat.MLSearch.Engine.OpenSearch.Extensions;

internal static class OpenSearchConfigurationExt
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

        services.AddSingleton<IndexNames>();
        services.AddSingleton(_ => new OpenSearchNamingPolicy(JsonNamingPolicy.CamelCase));

        services.AddSingleton<IOpenSearchClient>(s => {
            var openSearchSettings = s.GetRequiredService<IOptions<OpenSearchSettings>>().Value;
            var connectionSettings = new ConnectionSettings(
                new SingleNodeConnectionPool(new Uri(openSearchSettings.ClusterUri)),
                sourceSerializer: (builtin, settings) => new OpenSearchJsonSerializer(builtin, settings));
            return new OpenSearchClient(connectionSettings);
        });

        services.AddSingleton<ServiceCoordinator>()
            .AddAlias<IServiceCoordinator, ServiceCoordinator>()
            .AddHostedService(c => c.GetRequiredService<ServiceCoordinator>());

        services
            .AddSingleton<IClusterSetup>(static services => services.CreateInstanceWith<ClusterSetup>(
                services.GetRequiredService<IMeshLocks<MLSearchDbContext>>().WithKeyPrefix(nameof(ClusterSetup))
            ))
            .AddSingleton<IClusterSetupActions, ClusterSetupActions>();

        services
            .AddSingleton<IOptionsFactory<PlainIndexSettings>, PlainIndexSettingsFactory>()
            .AddSingleton<IOptionsFactory<SemanticIndexSettings>, SemanticIndexSettingsFactory>();

        services
            .AddSingleton(static services
                => services.CreateInstanceWith<SettingsChangeTokenSource<SemanticIndexSettings>>(IndexNames.ChatContent))
            .AddAlias<ISettingsChangeTokenSource, SettingsChangeTokenSource<SemanticIndexSettings>>()
            .AddAlias<IOptionsChangeTokenSource<SemanticIndexSettings>, SettingsChangeTokenSource<SemanticIndexSettings>>();

        foreach (var indexName in new[] { IndexNames.ChatCursor, IndexNames.ChatContentCursor }) {
            services
                .AddSingleton(s => s.CreateInstanceWith<SettingsChangeTokenSource<PlainIndexSettings>>(indexName))
                .AddAlias<ISettingsChangeTokenSource, SettingsChangeTokenSource<PlainIndexSettings>>()
                .AddAlias<IOptionsChangeTokenSource<PlainIndexSettings>, SettingsChangeTokenSource<PlainIndexSettings>>();
        }

        // ChatSlice engine registrations
        services.AddSingleton<ISearchEngine<ChatSlice>>(static services
            => services.CreateInstanceWith<SemanticSearchEngine<ChatSlice>>(IndexNames.ChatContent));

        return services;
    }
}
