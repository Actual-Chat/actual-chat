using ActualChat.Hosting;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using ActualChat.MLSearch.Module;
using ActualChat.Testing.Host;
using OpenSearch.Client;
using OpenSearch.Net;
using HttpMethod = OpenSearch.Net.HttpMethod;

namespace ActualChat.MLSearch.IntegrationTests;

[CollectionDefinition(nameof(MLSearchCollection))]
public class MLSearchCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink)
    : Testing.Host.AppHostFixture("ml_search", messageSink, TestAppHostOptions.Default with {
        ConfigureHost = (_, cfg) => {
            cfg.AddInMemoryCollection(($"{nameof(MLSearchSettings)}:{nameof(MLSearchSettings.IsEnabled)}", "true"));
            cfg.AddInMemoryCollection(($"{nameof(MLSearchSettings)}:{nameof(MLSearchSettings.IsInitialIndexingDisabled)}", "true"));
        },
        ConfigureServices = (_, services) => {
            services.AddSingleton<OpenSearchInit>()
                .AddAlias<IModuleInitializer, OpenSearchInit>();
            services.AddSingleton<OpenSearchCleanup>();
        }
    })
{
    public override async Task<TestAppHost> NewAppHost(Func<TestAppHostOptions, TestAppHostOptions>? optionOverrider = null)
    {
        var appHost = await base.NewAppHost(optionOverrider);
        // Ensure cleanup service is instantiated
        _ = appHost.Services.GetRequiredService<OpenSearchCleanup>();
        return appHost;
    }
}

#pragma warning disable CA1812
// An instance of OpenSearchInit class is created via DI container on app start
internal sealed class OpenSearchInit(IClusterSetup clusterSetup) : IModuleInitializer
{
    public Task Initialize(CancellationToken cancellationToken) => clusterSetup.InitializeAsync(cancellationToken);
}

// An instance of OpenSearchCleanup class is created via DI container of the app host of MLSearchCollection above

internal sealed class OpenSearchCleanup(IOpenSearchClient openSearch) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await openSearch.LowLevel.DoRequestAsync<StringResponse>(
            HttpMethod.DELETE, $"/{IndexNames.MLTestIndexPattern}", CancellationToken.None);
        await openSearch.LowLevel.DoRequestAsync<StringResponse>(
            HttpMethod.DELETE, $"/_ingest/pipeline/{IndexNames.MLTestIndexPattern}", CancellationToken.None);
    }
}

#pragma warning restore CA1812
