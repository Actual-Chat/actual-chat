using System.Security.Cryptography.X509Certificates;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Configuration;
using ActualChat.MLSearch.Engine.OpenSearch.Serializer;
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
        HostInfo hostInfo,
        MLSearchSettings settings)
    {
        services.AddOptionsWithValidateOnStart<OpenSearchSettings>()
            .Bind(cfg.GetSection($"{nameof(MLSearchSettings)}:{MLSearchSettings.OpenSearch}"))
            .ValidateDataAnnotations()
            .PostConfigure(options => {
                if (options.DefaultNumberOfReplicas is null && hostInfo.IsDevelopmentInstance)
                    options.DefaultNumberOfReplicas = 0;
            });

        services.AddSingleton(c => new OpenSearchNames {
            Env = settings.OpenSearchNamesEnvPrefix.NullIfEmpty() ?? (hostInfo.IsProductionInstance ? "" : "dev"),
        });
        services.AddSingleton(_ => new OpenSearchNamingPolicy(JsonNamingPolicy.CamelCase));

        services.AddSingleton<IOpenSearchClient>(s => {
            var openSearchSettings = s.GetRequiredService<IOptions<OpenSearchSettings>>().Value;
            var connectionSettings = new ConnectionSettings(
                    new SingleNodeConnectionPool(new Uri(openSearchSettings.ClusterUri)),
                    sourceSerializer: (builtin, connectionSettings) => new OpenSearchJsonSerializer(builtin, connectionSettings))
                .DefaultFieldNameInferrer(JsonNamingPolicy.CamelCase.ConvertName)
                .DefaultMappingFor<IndexedChat>(map => map.RoutingProperty(x => x.Id))
                .DefaultMappingFor<IndexedEntry>(map => map.RoutingProperty(x => x.ChatId))
                .DefaultMappingFor<IndexedUser>(map => map.RoutingProperty(x => x.Id))
                .DefaultMappingFor<IndexedUserContact>(map => map.RoutingProperty(x => x.OtherUserId));
            if (!openSearchSettings.User.IsNullOrEmpty() && !openSearchSettings.Password.IsNullOrEmpty())
                connectionSettings.BasicAuthentication(openSearchSettings.User, openSearchSettings.Password);
            else if (!openSearchSettings.ClientCertificatePath.IsNullOrEmpty()) {
                var certPath = Path.Combine(openSearchSettings.ClientCertificatePath, "tls.crt");
                var keyPath = Path.Combine(openSearchSettings.ClientCertificatePath, "tls.key");
                connectionSettings.ClientCertificate(X509Certificate2.CreateFromPemFile(certPath, keyPath));
            }
            return new OpenSearchClient(connectionSettings);
        });

        return services;
    }
}
