using ActualChat.MLSearch;
using ActualChat.MLSearch.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

// Search needs OpenSearch, which the default test host doesn't enable; only [LocalFact] tests use this collection
[CollectionDefinition(nameof(McpSearchCollection))]
public class McpSearchCollection : ICollectionFixture<McpSearchCollection.AppHostFixture>
{
    public sealed class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture("mcp_search", messageSink, TestAppHostOptions.Default with {
            ConfigureHost = (_, cfg) => cfg.AddInMemory<MLSearchSettings>(
                (x => x.IsEnabled, "true"),
                (x => x.ChangedEntityIndexingDelay, "00:00:04"),
                (x => x.IndexingFlowResumeDelayQuanta, "00:00:01.5")),
            ConfigureServices = (_, services) => services
                .AddSingleton<OpenSearchInit>()
                .AddAlias<IModuleInitializer, OpenSearchInit>(),
        });

    private sealed class OpenSearchInit(OpenSearchConfigurator configurator) : IModuleInitializer
    {
        public Task Initialize(CancellationToken cancellationToken)
            => configurator.InitializeAsync(cancellationToken);
    }
}
