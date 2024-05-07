using ActualChat.Hosting;
using ActualChat.MLSearch.ApiAdapters;
using ActualChat.MLSearch.Documents;
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

        services
            .AddSingleton<ClusterSetup>()
            .AddAlias<IModuleInitializer, ClusterSetup>();

        services
            .AddSingleton<IOptionsFactory<PlainIndexSettings>, PlainIndexSettingsFactory>()
            .AddSingleton<IOptionsFactory<SemanticIndexSettings>, SemanticIndexSettingsFactory>();

        services
            .AddSingleton(static services
                => services.CreateInstanceWith<IndexSettingsChangeTokenSource<SemanticIndexSettings>>(IndexNames.ChatContent))
            .AddAlias<IIndexSettingsChangeTokenSource, IndexSettingsChangeTokenSource<SemanticIndexSettings>>()
            .AddAlias<IOptionsChangeTokenSource<SemanticIndexSettings>, IndexSettingsChangeTokenSource<SemanticIndexSettings>>();

        foreach (var indexName in new[] { IndexNames.ChatCursor, IndexNames.ChatContentCursor }) {
            services
                .AddSingleton(s => s.CreateInstanceWith<IndexSettingsChangeTokenSource<PlainIndexSettings>>(indexName))
                .AddAlias<IIndexSettingsChangeTokenSource, IndexSettingsChangeTokenSource<PlainIndexSettings>>()
                .AddAlias<IOptionsChangeTokenSource<PlainIndexSettings>, IndexSettingsChangeTokenSource<PlainIndexSettings>>();
        }

        // ChatSlice engine registrations
        services.AddSingleton<ISearchEngine<ChatSlice>>(static services
            => services.CreateInstanceWith<OpenSearchEngine<ChatSlice>>(IndexNames.ChatContent));

        return services;
    }
}
